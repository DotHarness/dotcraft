import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { createElement, act } from 'react'
import { createRoot } from 'react-dom/client'
import { ComposerMascot } from '../dist/react.js'

test('Composer swaps only at the midpoint, cancels superseded names and disposes timers', async () => {
  const dom = new JSDOM('<div id="root"></div>', { pretendToBeVisual: true })
  const keys = ['window', 'document', 'MutationObserver', 'requestAnimationFrame', 'cancelAnimationFrame', 'IS_REACT_ACT_ENVIRONMENT']
  const previous = Object.fromEntries(keys.map(key => [key, globalThis[key]]))
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, MutationObserver: dom.window.MutationObserver, requestAnimationFrame: dom.window.requestAnimationFrame.bind(dom.window), cancelAnimationFrame: dom.window.cancelAnimationFrame.bind(dom.window), IS_REACT_ACT_ENVIRONMENT: true })
  window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} })
  let now = 0, sequence = 0
  const timers = new Map()
  window.setTimeout = (callback, delay) => { const id = ++sequence; timers.set(id, {callback, at: now + delay}); return id }
  window.clearTimeout = id => timers.delete(id)
  const advance = async duration => {
    const end = now + duration
    for (;;) {
      const pending = [...timers].filter(([,t]) => t.at <= end).sort((a,b) => a[1].at-b[1].at)[0]
      if (!pending) break
      now = pending[1].at; timers.delete(pending[0]); await act(async () => pending[1].callback())
    }
    now = end
  }
  const names = []
  const onNameRendered = name => names.push(name)
  const root = createRoot(document.querySelector('#root'))
  const render = props => act(async () => root.render(createElement(ComposerMascot, { focused: true, onNameRendered, ...props })))
  try {
    await render({name: ''})
    assert.equal(document.querySelector('[data-mascot-theme]').dataset.mascotTheme, 'dark')
    await render({name: '', theme: 'light'})
    assert.equal(document.querySelector('[data-mascot-theme]').dataset.mascotTheme, 'light')
    await render({name: '', theme: 'dark'})
    assert.equal(document.querySelector('[data-mascot-theme]').dataset.mascotTheme, 'dark')
    await render({name: 'Researcher'})
    assert.equal(document.querySelector('[data-mascot-profile-transition]').dataset.mascotProfileTransition, 'active')
    await advance(619); assert.equal(names.at(-1), '')
    await advance(1); assert.equal(names.at(-1), 'Researcher')
    await render({name: 'Night Shift'}); await advance(300)
    await render({name: '夜班助手'}); await advance(620)
    assert.equal(names.at(-1), '夜班助手')
    await advance(1000); assert.equal(names.at(-1), '夜班助手')
    await render({name: '', motion: 'off'})
    assert.equal(names.at(-1), '')
    assert.equal(document.querySelector('[data-mascot-profile-transition]').dataset.mascotProfileTransition, 'idle')
    await render({name: 'default', motion: 'off'})
    assert.equal(names.at(-1), 'default')
    await render({name: 'Researcher', motion: 'off', interaction: {expression: 'sleep'}})
    assert.equal(document.querySelector('.dca-robot').dataset.pose, 'sleep')
  } finally {
    await act(async () => root.unmount())
    assert.equal(timers.size, 0)
    dom.window.close()
    for (const key of keys) if (previous[key] === undefined) delete globalThis[key]; else globalThis[key] = previous[key]
  }
})
