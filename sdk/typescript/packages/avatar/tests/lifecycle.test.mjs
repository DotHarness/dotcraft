import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { createElement } from 'react'
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { Avatar } from '../dist/react.js'

globalThis.IS_REACT_ACT_ENVIRONMENT = true

function createEnvironment({ reduced = false, legacyMedia = false, matchMedia = true } = {}) {
  const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'https://avatar.test/' })
  const previous = Object.fromEntries(['window', 'document', 'IntersectionObserver', 'requestAnimationFrame', 'cancelAnimationFrame'].map(key => [key, globalThis[key]]))
  globalThis.window = dom.window
  globalThis.document = dom.window.document
  Object.defineProperty(dom.window.document, 'hidden', { configurable: true, value: false })

  let nextFrame = 1
  let requestedFrames = 0
  const frames = new Map()
  const cancelledFrames = []
  globalThis.requestAnimationFrame = dom.window.requestAnimationFrame = callback => {
    requestedFrames++
    const id = nextFrame++
    frames.set(id, callback)
    return id
  }
  globalThis.cancelAnimationFrame = dom.window.cancelAnimationFrame = id => {
    cancelledFrames.push(id)
    frames.delete(id)
  }

  const observers = []
  globalThis.IntersectionObserver = dom.window.IntersectionObserver = class {
    constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this) }
    observe(target) { this.target = target; this.callback([{ isIntersecting: true, target }]) }
    disconnect() { this.disconnected = true }
  }

  const mediaListeners = new Set()
  const media = {
    matches: reduced,
    addEventListener: legacyMedia ? undefined : (_type, listener) => mediaListeners.add(listener),
    removeEventListener: legacyMedia ? undefined : (_type, listener) => mediaListeners.delete(listener),
    addListener: legacyMedia ? listener => mediaListeners.add(listener) : undefined,
    removeListener: legacyMedia ? listener => mediaListeners.delete(listener) : undefined,
  }
  if (matchMedia) dom.window.matchMedia = () => media
  else dom.window.matchMedia = undefined

  const root = createRoot(dom.window.document.querySelector('#root'))
  const render = async props => act(async () => root.render(createElement(Avatar, props)))
  const flushFrame = async now => act(async () => {
    const pending = [...frames.values()]
    frames.clear()
    for (const callback of pending) callback(now)
  })
  const setReduced = async value => act(async () => {
    media.matches = value
    for (const listener of [...mediaListeners]) listener({ matches: value })
  })
  const cleanup = async () => {
    await act(async () => root.unmount())
    dom.window.close()
    for (const [key, value] of Object.entries(previous)) {
      if (value === undefined) delete globalThis[key]
      else globalThis[key] = value
    }
  }
  return { dom, render, root, flushFrame, setReduced, frames, cancelledFrames, observers, mediaListeners, cleanup, get requestedFrames() { return requestedFrames } }
}

function identity(container) {
  const avatar = container.querySelector('.dca-robot')
  return [avatar.dataset.primary, avatar.dataset.secondary, avatar.dataset.baseFace]
}

function decoration(container) {
  return container.querySelector('[data-decoration-motion] > g')
}

test('renaming derives a new identity without saving avatar state', async () => {
  const env = createEnvironment()
  try {
    await env.render({ name: 'agent', size: 44 })
    const before = identity(env.dom.window.document)
    await env.render({ name: 'renamed', size: 44 })
    assert.notDeepEqual(identity(env.dom.window.document), before)
    assert.equal(env.dom.window.localStorage.length, 0)
  } finally { await env.cleanup() }
})

test('incrementing eventSequence retriggers the same event pose', async () => {
  const env = createEnvironment()
  try {
    await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'on', size: 44 })
    assert.ok(env.frames.size > 0)
    await env.flushFrame(0)
    assert.ok(env.frames.size > 0)
    await env.flushFrame(320)
    assert.match(decoration(env.dom.window.document).getAttribute('transform'), /translate\(0 -[1-9]/)
    await env.flushFrame(800)
    await env.render({ name: 'leader', state: 'done', eventSequence: 2, motion: 'on', size: 44 })
    await env.flushFrame(1000)
    await env.flushFrame(1320)
    assert.match(decoration(env.dom.window.document).getAttribute('transform'), /translate\(0 -[1-9]/)
  } finally { await env.cleanup() }
})

test('pause freezes the airborne DOM frame and resume continues it', async () => {
  const env = createEnvironment()
  try {
    await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'on', size: 44 })
    assert.ok(env.frames.size > 0)
    await env.flushFrame(0)
    await env.flushFrame(320)
    const airborne = decoration(env.dom.window.document).getAttribute('transform')
    assert.match(airborne, /translate\(0 -[1-9]/)
    await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'on', paused: true, size: 44 })
    await env.flushFrame(10000)
    assert.equal(decoration(env.dom.window.document).getAttribute('transform'), airborne)
    await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'on', paused: false, size: 44 })
    await env.flushFrame(10000)
    await env.flushFrame(10480)
    assert.notEqual(decoration(env.dom.window.document).getAttribute('transform'), airborne)
  } finally { await env.cleanup() }
})

test('reduced motion resets decoration', async () => {
  const env = createEnvironment()
  await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'system', size: 44 })
  await env.flushFrame(0)
  await env.flushFrame(320)
  assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, 'on')
  await env.setReduced(true)
  assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, 'off')
  assert.match(decoration(env.dom.window.document).getAttribute('transform'), /^translate\(0 0\)/)
  assert.ok(env.mediaListeners.size > 0)
  await env.cleanup()
  assert.equal(env.mediaListeners.size, 0)
  assert.ok(env.observers.every(observer => observer.disconnected))
})

test('unmount cancels subscriptions, observation, and scheduled animation frames', async () => {
  const env = createEnvironment()
  await env.render({ name: 'leader', state: 'done', eventSequence: 1, motion: 'on', size: 44 })
  await env.cleanup()
  assert.equal(env.mediaListeners.size, 0)
  assert.ok(env.observers.every(observer => observer.disconnected))
  assert.ok(env.requestedFrames > 0)
  assert.ok(env.cancelledFrames.length > 0)
})

test('missing matchMedia and legacy media listeners are both safe', async () => {
  for (const options of [{ matchMedia: false }, { legacyMedia: true }]) {
    const env = createEnvironment(options)
    try {
      await env.render({ name: 'agent', motion: 'system', size: 44 })
      assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, options.matchMedia === false ? 'off' : 'on')
      if (options.legacyMedia) {
        await env.setReduced(true)
        assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, 'off')
      }
    } finally { await env.cleanup() }
  }
})

test('expression changes retain the base and happy DOM layers without retracting decorations', async () => {
  const env = createEnvironment()
  try {
    await env.render({ name: 'agent', expression: 'base', motion: 'on', size: 44 })
    const robot = env.dom.window.document.querySelector('.dca-robot')
    const base = robot.querySelector('.dca-part-face-neutral')
    const happy = robot.querySelector('.dca-part-face-happy')
    const accessory = robot.querySelector('[data-accessory]')
    await env.render({ name: 'agent', expression: 'happy', motion: 'on', size: 44 })
    assert.equal(robot.querySelector('.dca-part-face-neutral'), base)
    assert.equal(robot.querySelector('.dca-part-face-happy'), happy)
    assert.equal(robot.querySelector('[data-accessory]'), accessory)
    assert.equal(robot.dataset.exiting, 'false')
  } finally { await env.cleanup() }
})

test('gesture replay pauses active time and cancels superseded completion', async () => {
  const env = createEnvironment()
  const completed = []
  const props = { name: 'agent', gesture: 'blink', gestureSequence: 1, onGestureComplete: sequence => completed.push(sequence), motion: 'on', size: 44 }
  try {
    await env.render(props)
    await env.flushFrame(0)
    await env.flushFrame(160)
    await env.render({ ...props, paused: true })
    await env.flushFrame(10000)
    assert.deepEqual(completed, [])
    await env.render({ ...props, paused: false })
    await env.flushFrame(10000)
    await env.flushFrame(10400)
    assert.deepEqual(completed, [1])

    await env.render({ ...props, gestureSequence: 2 })
    await env.flushFrame(11000)
    await env.flushFrame(11600)
    assert.deepEqual(completed, [1, 2])

    await env.render({ ...props, gesture: 'look-left', gestureSequence: 3 })
    await env.flushFrame(12000)
    await env.flushFrame(12400)
    await env.render({ ...props, gesture: 'look-right', gestureSequence: 4 })
    await env.flushFrame(12500)
    await env.flushFrame(13700)
    assert.deepEqual(completed, [1, 2, 4])
  } finally { await env.cleanup() }
})

test('work states stow a held accessory without replacing it and idle restores it', async () => {
  const env = createEnvironment()
  try {
    const props = { name: 'Fixture 5', motion: 'on', size: 44 }
    await env.render({ ...props, state: 'idle' })
    const robot = env.dom.window.document.querySelector('.dca-robot')
    const accessory = robot.querySelector('[data-accessory="shield"]')
    const held = robot.querySelector('[data-held-decoration="shield"]')
    assert.equal(robot.dataset.heldState, 'holding')
    const arm = held.closest('.dca-part-arm-l')
    assert.ok(arm?.contains(robot.querySelector('.dca-part-arm-l-b')))
    await env.render({ ...props, state: 'done' })
    assert.equal(held.closest('.dca-part-arm-l'), arm)
    assert.equal(robot.dataset.heldState, 'holding')
    await env.render({ ...props, state: 'working' })
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 170)) })
    assert.equal(robot.dataset.heldState, 'stowed')
    assert.equal(robot.querySelector('[data-accessory="shield"]'), accessory)
    assert.equal(robot.querySelector('[data-held-decoration="shield"]'), held)
    await env.render({ ...props, state: 'waiting' })
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 170)) })
    assert.equal(robot.dataset.heldState, 'stowed')
    assert.equal(robot.querySelector('[data-accessory="shield"]'), accessory)
    await env.render({ ...props, state: 'idle', motion: 'off' })
    assert.equal(robot.dataset.heldState, 'holding')
    assert.equal(robot.querySelector('[data-accessory="shield"]'), accessory)
  } finally { await env.cleanup() }
})
