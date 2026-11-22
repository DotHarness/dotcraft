import assert from 'node:assert/strict'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import {
  AppearanceAvatar, AvatarCatalogProvider, AvatarOutfitProvider, DecorationSwatch, Silhouette, createAvatarCatalog, defineAvatarPack,
} from '../dist/react.js'
import { originalAppearance } from '../dist/index.js'

test('multiple avatars have disjoint paint, clip and filter IDs', () => {
  const html = renderToStaticMarkup(createElement('div', null, ...Array.from({ length: 6 }, (_, index) => createElement(AppearanceAvatar, { appearance: { ...originalAppearance, head: 'ringed-planet', face: 'neon-visor', back: 'halo', skin: 'chrome' }, size: 64, key: index }))))
  const ids = [...html.matchAll(/ id="([^"]+)"/g)].map(match => match[1])
  assert.equal(ids.length, new Set(ids).size)
})

test('external component packs invoke registered slot renderers and unmounted ids degrade without throwing', () => {
  const rendered = []
  const pack = defineAvatarPack({
    namespace: 'ext',
    label: 'External Test Pack',
    items: [
      {
        id: 'ext:twintails',
        slot: 'head',
        rarity: 'legendary',
        series: 'costume',
        zones: ['top'],
        render: () => { rendered.push('front'); return createElement(Silhouette, { d: 'M360 360h304v40H360Z', fill: '#ff7aa2' }) },
        renderBack: () => { rendered.push('back'); return createElement(Silhouette, { d: 'M120 420h100v260H120Z', fill: '#e35d88' }) },
      },
      {
        id: 'ext:suit',
        slot: 'skin',
        kind: 'paint',
        rarity: 'epic',
        series: 'costume',
        zones: ['body'],
        paintMaterial: { shadow: '#3a1024', accent: '#ff7aa2' },
        renderPaint: id => { rendered.push('paint'); return createElement('linearGradient', { id }, createElement('stop', { offset: '0', stopColor: '#ff9ebb' })) },
      },
    ],
  })
  const catalog = createAvatarCatalog({ packs: [pack] })
  const look = { ...originalAppearance, head: 'ext:twintails', skin: 'ext:suit' }

  assert.doesNotThrow(() => renderToStaticMarkup(createElement(AppearanceAvatar, { appearance: look, size: 64 })))
  assert.deepEqual(rendered, [])

  const swatchHtml = renderToStaticMarkup(
    createElement(AvatarCatalogProvider, { catalog },
      createElement(AppearanceAvatar, { appearance: look, size: 64 }),
      createElement(DecorationSwatch, { id: 'ext:twintails' }),
    ),
  )
  assert.ok(rendered.includes('front') && rendered.includes('back') && rendered.includes('paint'))
  assert.ok(swatchHtml.includes('aria-label="Twintails specimen"'))
})

test('an outfit dresses the stored appearance over an explicit catalog', () => {
  const pack = defineAvatarPack({
    namespace: 'role',
    label: 'Role Pack',
    items: [
      { id: 'role:tie', slot: 'face', kind: 'accessory', rarity: 'rare', series: 'costume', zones: ['rim'], render: () => createElement('path', { d: 'M500 820h24', 'data-role': 'tie' }) },
      { id: 'role:baton', slot: 'hand', rarity: 'rare', series: 'costume', zones: ['hand'], render: () => createElement('path', { d: 'M226 640v-200', 'data-role': 'baton' }) },
    ],
  })
  const stored = { ...originalAppearance, palette: 3, head: 'ringed-planet', face: 'bow-tie' }
  const html = renderToStaticMarkup(
    createElement(AvatarOutfitProvider, { pack, wear: { face: 'role:tie', hand: 'role:baton' } },
      createElement(AppearanceAvatar, { appearance: stored, catalog: createAvatarCatalog(), size: 64 }),
    ),
  )
  assert.ok(html.includes('data-role="tie"') && html.includes('data-role="baton"'))
  assert.ok(!html.includes('#e8654f'))
  assert.ok(html.includes('data-head="ringed-planet"'))
})

test('a conducting hand item stays in hand while working and replaces the laptop', () => {
  const pack = defineAvatarPack({
    namespace: 'role',
    label: 'Role Pack',
    items: [{ id: 'role:baton', slot: 'hand', rarity: 'rare', series: 'costume', zones: ['hand'], workStory: 'conduct', render: () => createElement('path', { d: 'M226 640v-200' }) }],
  })
  const catalog = createAvatarCatalog({ packs: [pack] })
  const working = appearance => renderToStaticMarkup(createElement(AppearanceAvatar, { appearance, catalog, state: 'working', size: 64 }))
  const conducting = working({ ...originalAppearance, hand: 'role:baton' })
  assert.ok(conducting.includes('data-held-state="holding"'))
  assert.ok(!conducting.includes('dca-action-prop-laptop'))
  const typing = working({ ...originalAppearance, hand: 'magic-wand' })
  assert.ok(typing.includes('data-held-state="stowed"') && typing.includes('dca-action-prop-laptop'))
})
