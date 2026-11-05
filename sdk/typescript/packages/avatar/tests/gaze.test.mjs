import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { act, createElement, useRef, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { useAvatarGaze } from '../dist/react.js'

globalThis.IS_REACT_ACT_ENVIRONMENT = true

async function mount(options, { reduced = false, touch = false } = {}) {
  const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'https://avatar.test/' })
  const { window } = dom
  const frames = []
  globalThis.window = window
  globalThis.document = window.document
  globalThis.matchMedia = query => ({ matches: query.includes('reduced-motion') ? reduced : !touch })
  globalThis.requestAnimationFrame = callback => frames.push(callback)
  globalThis.cancelAnimationFrame = id => { frames[id - 1] = null }
  let setEnabled
  function Host() {
    const face = useRef(null)
    const [area, setArea] = useState(null)
    const [enabled, set] = useState(options.enabled ?? true)
    setEnabled = set
    useAvatarGaze(face, { ...options, enabled, ...(options.tile ? { within: area } : {}) })
    return createElement('a', { id: 'tile', ref: setArea }, createElement('span', { id: 'face', ref: face }))
  }
  const root = createRoot(window.document.getElementById('root'))
  await act(async () => root.render(createElement(Host)))
  const face = window.document.getElementById('face')
  face.getBoundingClientRect = () => ({ left: 0, top: 0, width: 60, height: 60 })
  const fire = (target, type, clientX = 0, clientY = 0) => {
    const event = new window.Event(type)
    Object.assign(event, { clientX, clientY })
    target.dispatchEvent(event)
    for (const frame of frames.splice(0)) frame?.()
  }
  return {
    window, face,
    tile: window.document.getElementById('tile'),
    fire,
    gaze: () => [face.style.getPropertyValue('--dca-gaze-x'), face.style.getPropertyValue('--dca-gaze-y')],
    setEnabled: value => act(async () => setEnabled(value)),
    unmount: () => act(async () => root.unmount()),
  }
}

test('the gaze points at the pointer, stops at its limit and lets go past its range', async () => {
  const host = await mount({ reach: 100 })
  host.fire(host.window, 'pointermove', 80, 30)
  assert.deepEqual(host.gaze(), ['0.500', '0.000'])
  host.fire(host.window, 'pointermove', 30, 330)
  assert.deepEqual(host.gaze(), ['0.000', '1.000'])
  host.fire(host.window, 'pointermove', 1000, 30)
  assert.deepEqual(host.gaze(), ['', ''])
  await host.unmount()
})

test('a tile follows only inside itself and lets go when the pointer leaves', async () => {
  const host = await mount({ tile: true, reach: 40 })
  host.fire(host.window, 'pointermove', 70, 30)
  assert.deepEqual(host.gaze(), ['', ''])
  host.fire(host.tile, 'pointermove', 70, 30)
  assert.deepEqual(host.gaze(), ['1.000', '0.000'])
  host.fire(host.tile, 'pointerleave')
  assert.deepEqual(host.gaze(), ['', ''])
  await host.unmount()
})

test('turning it off lets go, and it follows again once turned back on', async () => {
  const host = await mount({ reach: 100 })
  host.fire(host.window, 'pointermove', 80, 30)
  await host.setEnabled(false)
  assert.deepEqual(host.gaze(), ['', ''])
  await host.setEnabled(true)
  host.fire(host.window, 'pointermove', 80, 30)
  assert.deepEqual(host.gaze(), ['0.500', '0.000'])
  await host.unmount()
})

for (const [name, environment] of [['reduced motion', { reduced: true }], ['a touch device', { touch: true }]]) {
  test(`${name} follows nothing`, async () => {
    const host = await mount({}, environment)
    host.fire(host.window, 'pointermove', 80, 30)
    assert.deepEqual(host.gaze(), ['', ''])
    await host.unmount()
  })
}
