import assert from 'node:assert/strict'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import {
  AppearanceAvatar, AvatarCatalogProvider, DecorationSwatch, Silhouette, createAvatarCatalog, defineAvatarPack,
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
