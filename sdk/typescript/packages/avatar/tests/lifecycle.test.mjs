import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { createElement } from 'react'
import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { Avatar } from '../dist/react.js'

globalThis.IS_REACT_ACT_ENVIRONMENT = true

function createEnvironment({ reduced = false, matchMedia = true } = {}) {
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
    addEventListener: (_type, listener) => mediaListeners.add(listener),
    removeEventListener: (_type, listener) => mediaListeners.delete(listener),
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

test('reduced motion turns system motion off and unmount cancels subscriptions, observation, and animation frames', async () => {
  const env = createEnvironment()
  await env.render({ name: 'Reviewer', state: 'done', eventSequence: 1, motion: 'system', size: 44 })
  await env.flushFrame(0)
  await env.flushFrame(320)
  assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, 'on')
  await env.setReduced(true)
  assert.equal(env.dom.window.document.querySelector('.dca-robot').dataset.motion, 'off')
  assert.ok(env.mediaListeners.size > 0)
  await env.cleanup()
  assert.equal(env.mediaListeners.size, 0)
  assert.ok(env.observers.every(observer => observer.disconnected))
  assert.ok(env.requestedFrames > 0)
  assert.ok(env.cancelledFrames.length > 0)
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
