import assert from 'node:assert/strict'
import test from 'node:test'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { Avatar, AppearanceAvatar, DecorationSwatch } from '../dist/react.js'
import { deriveAppearance, originalAppearance, compatibility, decorations } from '../dist/index.js'
const render=props=>renderToStaticMarkup(createElement(AppearanceAvatar,props))
const states=['idle','thinking','working','waiting','blocked','done','greeting','sleep']
test('SSR renders every compatible pose with original paired arm geometry', () => {
  for(const [primary, secondaries] of Object.entries(compatibility)) for(const secondary of secondaries) for(const state of states) {
    const html=render({appearance:{...deriveAppearance('agent'),primary,secondary},state,label:'Agent'})
    for(const [name,x,y,w,h,rx] of [['l-w',165,472,136,256,58],['r-w',723,472,136,256,58],['l-b',188,514,90,171,19],['r-b',746,514,90,171,19]]) {
      assert.ok(html.includes(`class="dca-part-arm-${name}" x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}"`))
    }
    assert.equal(html.includes('class="dca-part-light"'),primary==='none')
    assert.ok(html.includes('data-primary="'+primary+'"'))
    if(primary!=='none') assert.ok(html.includes('data-decoration="'+primary+'"'))
  }
})
test('unnamed avatar uses original paint; all art renders; compact accessories simplify', () => {
  const plain=renderToStaticMarkup(createElement(Avatar,{name:'',label:'DotCraft'}))
  assert.ok(plain.includes('#2458f7')); assert.ok(plain.includes('#5f82f7')); assert.ok(plain.includes('#8fa5ff'))
  assert.ok(plain.includes('aria-label="DotCraft"'))
  for(const {id} of decorations) assert.ok(renderToStaticMarkup(createElement(DecorationSwatch,{id})).includes('<svg'))
  for(const size of [16,20]) {
    const html=render({appearance:{...originalAppearance,primary:'rubber-duck',secondary:'shield'},size,motion:'on'})
    assert.ok(html.includes('data-motion="off"')); assert.ok(!html.includes('data-accessory='))
  }
})
test('multiple avatars have disjoint paint and clip IDs', () => {
  const html=renderToStaticMarkup(createElement('div',null,...Array.from({length:8},()=>createElement(Avatar,{name:'Agent'}))))
  const ids=[...html.matchAll(/ id="([^"]+)"/g)].map(m=>m[1])
  assert.equal(ids.length,new Set(ids).size)
})
