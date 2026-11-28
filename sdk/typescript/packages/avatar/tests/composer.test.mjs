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
  } finally {
    await act(async () => root.unmount())
    assert.equal(timers.size, 0)
    dom.window.close()
    for (const key of keys) if (previous[key] === undefined) delete globalThis[key]; else globalThis[key] = previous[key]
  }
})

test('Composer listening suppresses active idle and sleep and cancels an active trip', async t => {
  const dom = new JSDOM('<div id="root"></div>', { pretendToBeVisual: true })
  const keys = ['window', 'document', 'MutationObserver', 'requestAnimationFrame', 'cancelAnimationFrame', 'IS_REACT_ACT_ENVIRONMENT']
  const previous = Object.fromEntries(keys.map(key => [key, globalThis[key]]))
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, MutationObserver: dom.window.MutationObserver, requestAnimationFrame: dom.window.requestAnimationFrame.bind(dom.window), cancelAnimationFrame: dom.window.cancelAnimationFrame.bind(dom.window), IS_REACT_ACT_ENVIRONMENT: true })
  window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} })
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 1_000_000 })
  t.mock.method(window, 'setTimeout', globalThis.setTimeout)
  t.mock.method(window, 'clearTimeout', globalThis.clearTimeout)
  t.mock.method(Math, 'random', () => 0)
  const root = createRoot(document.querySelector('#root'))
  const render = props => act(async () => root.render(createElement(ComposerMascot, { motion: 'on', ...props })))
  const advance = duration => act(async () => t.mock.timers.tick(duration))
  const mascot = () => document.querySelector('[data-composer-avatar-pose]')
  try {
    await render({ listening: true })
    await advance(120_000)
    assert.equal(mascot().dataset.mascotActiveIdle, undefined)
    assert.equal(mascot().dataset.composerAvatarPose, 'idle')

    await render({ listening: false })
    await advance(34_999)
    assert.equal(mascot().dataset.mascotActiveIdle, undefined)
    await advance(1)
    assert.equal(mascot().dataset.mascotActiveIdle, 'hop')

    await render({ listening: true })
    assert.equal(mascot().dataset.mascotActiveIdle, undefined)

    await render({ listening: false })
    await advance(35_000)
    assert.equal(mascot().dataset.mascotActiveIdle, 'rocket')
    await advance(1_960)
    await advance(1_400)
    await advance(1_960)
    assert.equal(mascot().dataset.mascotActiveIdle, undefined)
    await advance(90_000)
    assert.equal(mascot().dataset.composerAvatarPose, 'sleep')

    await render({ listening: true })
    assert.equal(mascot().dataset.composerAvatarPose, 'idle')
  } finally {
    await act(async () => root.unmount())
    t.mock.restoreAll()
    t.mock.timers.reset()
    dom.window.close()
    for (const key of keys) if (previous[key] === undefined) delete globalThis[key]; else globalThis[key] = previous[key]
  }
})
