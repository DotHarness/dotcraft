import assert from 'node:assert/strict'
import test from 'node:test'
import { JSDOM } from 'jsdom'
import { createElement, act } from 'react'
import { createRoot } from 'react-dom/client'
import { useComposerAvatarBehavior } from '../dist/composer/useComposerAvatarBehavior.js'

test('Composer gestures and receipt feedback preserve task state', async t => {
  const dom = new JSDOM('<div id="root"></div>', { pretendToBeVisual: true })
  const keys = ['window', 'document', 'IS_REACT_ACT_ENVIRONMENT']
  const previous = Object.fromEntries(keys.map(key => [key, globalThis[key]]))
  Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true })
  t.mock.timers.enable({ apis: ['setTimeout'] })
  t.mock.method(window, 'setTimeout', globalThis.setTimeout)
  t.mock.method(window, 'clearTimeout', globalThis.clearTimeout)
  t.mock.method(Math, 'random', () => 0)
  const defaults = { semanticPose: 'idle', baseExpression: 'neutral', focused: false, dragOver: false, sleeping: false, waving: false, activeIdle: false, bounceSignal: 0, reducedMotion: false }
  let behavior
  function Probe(props) { behavior = useComposerAvatarBehavior(props); return null }
  const root = createRoot(document.querySelector('#root'))
  const render = props => act(async () => root.render(createElement(Probe, { ...defaults, ...props })))
  const advance = duration => act(async () => t.mock.timers.tick(duration))
  try {
    await render({ focused: true })
    assert.equal(behavior.pose, 'idle')
    assert.equal(behavior.expression, 'happy')
    await render({ dragOver: true })
    assert.equal(behavior.pose, 'idle')
    assert.equal(behavior.expression, 'operator')

    await render({})
    await advance(2600)
    assert.equal(behavior.gesture, 'blink')
    assert.equal(behavior.gestureSequence, 1)
    await act(async () => behavior.completeGesture(0))
    assert.equal(behavior.gesture, 'blink')
    await act(async () => behavior.completeGesture(1))
    assert.equal(behavior.gesture, undefined)
    await advance(2600)
    assert.equal(behavior.gesture, 'blink')
    assert.equal(behavior.gestureSequence, 2)

    await render({ bounceSignal: 1 })
    assert.equal(behavior.pose, 'acknowledge')
    await advance(350)
    assert.equal(behavior.pose, 'idle')
    await render({ bounceSignal: 2 })
    await render({ semanticPose: 'working', bounceSignal: 2 })
    assert.equal(behavior.pose, 'working')
    await render({ bounceSignal: 2 })
    assert.equal(behavior.pose, 'idle')
    for (const semanticPose of ['waiting', 'working', 'blocked', 'done']) {
      await render({ semanticPose, focused: true, dragOver: true })
      assert.equal(behavior.pose, semanticPose)
      assert.equal(behavior.expression, undefined)
    }
    await render({ reducedMotion: true })
    await advance(10000)
    assert.equal(behavior.gesture, undefined)
  } finally {
    await act(async () => root.unmount())
    t.mock.restoreAll()
    t.mock.timers.reset()
    dom.window.close()
    for (const key of keys) if (previous[key] === undefined) delete globalThis[key]; else globalThis[key] = previous[key]
  }
})
