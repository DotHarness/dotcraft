import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { createElement, act } from 'react'
import { createRoot } from 'react-dom/client'
import { useMascotActiveIdle } from '../dist/composer/useMascotActiveIdle.js'

function harness(t, random = 0) {
  const dom = new JSDOM('<div id="root"></div>', { pretendToBeVisual: true })
  const keys = ['window', 'document', 'IS_REACT_ACT_ENVIRONMENT']
  const previous = Object.fromEntries(keys.map(key => [key, globalThis[key]]))
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
  t.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 1_000_000 })
  t.mock.method(window, 'setTimeout', globalThis.setTimeout)
  t.mock.method(window, 'clearTimeout', globalThis.clearTimeout)
  const state = { random, idle: null, started: 0, activity: 0 }
  t.mock.method(Math, 'random', () => state.random)
  function Probe(props) { state.idle = useMascotActiveIdle(props); return null }
  const root = createRoot(document.querySelector('#root'))
  const base = { enabled: true, onStart: () => { state.started++ }, onActivity: () => { state.activity++ } }
  return {
    state,
    render: props => act(async () => root.render(createElement(Probe, { ...base, ...props }))),
    advance: duration => act(async () => t.mock.timers.tick(duration)),
    fire: type => act(async () => { window.dispatchEvent(new window.Event(type)) }),
    finish: animationName => act(async () => state.idle.onAnimationEnd({ animationName })),
    restore: async () => { await act(async () => root.unmount()); Object.assign(globalThis, previous) }
  }
}

test('active idle launches after the quiet window and walks outbound, away, inbound', async t => {
  const h = harness(t)
  try {
    await h.render({})
    await h.advance(34_999)
    assert.equal(h.state.idle.activeIdle, null)
    await h.advance(1)
    assert.deepEqual(h.state.idle.activeIdle, { motion: 'hop', phase: 'outbound', direction: 'left', travel: 'left' })
    assert.equal(h.state.started, 1)
    assert.equal(h.state.idle.className, 'composer-mascot-active-idle')
    assert.deepEqual(h.state.idle.attributes, {
      'data-mascot-active-idle': 'hop', 'data-mascot-idle-phase': 'outbound',
      'data-mascot-idle-direction': 'left', 'data-mascot-idle-travel': 'left'
    })
    await h.advance(2_560)
    assert.equal(h.state.idle.activeIdle.phase, 'away')
    await h.advance(1_400)
    assert.deepEqual(h.state.idle.activeIdle, { motion: 'hop', phase: 'inbound', direction: 'left', travel: 'right' })
    await h.advance(2_560)
    assert.equal(h.state.idle.activeIdle, null)
    assert.equal(h.state.idle.className, undefined)
    assert.deepEqual(h.state.idle.attributes, {})

    // Activity restarts the clock; the same roll then yields the alternate motion.
    await h.fire('keydown')
    await h.advance(35_000)
    assert.equal(h.state.idle.activeIdle.motion, 'rocket')
    await h.finish('composer-mascot-idle-hop-travel')
    assert.equal(h.state.idle.activeIdle.phase, 'outbound')
    await h.finish('composer-mascot-idle-rocket-flight-x')
    assert.equal(h.state.idle.activeIdle.phase, 'away')
    await h.advance(1_400)
    assert.equal(h.state.idle.activeIdle.phase, 'inbound')
    await h.finish('composer-mascot-idle-rocket-flight-x')
    assert.equal(h.state.idle.activeIdle, null)
  } finally {
    await h.restore()
  }
})

test('activity cancels a trip and defers the next; pointer moves can be non-interrupting', async t => {
  const h = harness(t)
  try {
    await h.render({})
    await h.advance(35_000)
    assert.notEqual(h.state.idle.activeIdle, null)
    const revision = h.state.idle.activityRevision
    await h.fire('keydown')
    assert.equal(h.state.idle.activeIdle, null)
    assert.equal(h.state.idle.activityRevision, revision + 1)
    assert.equal(h.state.activity, 1)
    await h.fire('keydown')
    assert.equal(h.state.idle.activityRevision, revision + 1)
    assert.equal(h.state.activity, 2)
    await h.advance(34_999)
    assert.equal(h.state.idle.activeIdle, null)
    await h.advance(1)
    assert.notEqual(h.state.idle.activeIdle, null)

    await h.render({ interruptOnPointerMove: false })
    await h.advance(600)
    await h.fire('pointermove')
    assert.notEqual(h.state.idle.activeIdle, null)
    assert.equal(h.state.idle.activityRevision, revision + 2)
    await h.fire('pointermove')
    assert.equal(h.state.idle.activityRevision, revision + 2)
    await h.fire('pointerdown')
    assert.equal(h.state.idle.activeIdle, null)
  } finally {
    await h.restore()
  }
})

test('the launch plan picks direction and motion pool, and a null plan skips the round', async t => {
  const h = harness(t, 0.95)
  try {
    let allow = false
    await h.render({ plan: () => (allow ? { direction: 'right', motions: ['hop', 'rocket'] } : null) })
    await h.advance(63_500)
    assert.equal(h.state.idle.activeIdle, null)
    assert.equal(h.state.started, 0)
    allow = true
    await h.advance(63_500)
    assert.deepEqual(h.state.idle.activeIdle, { motion: 'hop', phase: 'outbound', direction: 'right', travel: 'right' })
    await h.advance(2_560)
    await h.advance(1_400)
    assert.deepEqual(h.state.idle.activeIdle, { motion: 'hop', phase: 'inbound', direction: 'right', travel: 'left' })
    await h.render({ enabled: false })
    assert.equal(h.state.idle.activeIdle, null)
    await h.advance(200_000)
    assert.equal(h.state.idle.activeIdle, null)
  } finally {
    await h.restore()
  }
})
