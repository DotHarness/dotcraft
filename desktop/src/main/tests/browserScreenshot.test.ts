import { EventEmitter } from 'node:events'
import { afterEach, expect, it, vi } from 'vitest'
import { BrowserScreenshot, type BrowserScreenshotContext } from '../browserScreenshot'

function fixture(options: { ratio?: unknown; visible?: boolean } = {}) {
  const capture = new BrowserScreenshot()
  const debuggerApi = new EventEmitter()
  const page = Object.assign(new EventEmitter(), {
    debugger: debuggerApi, isDestroyed: () => false, getURL: () => 'http://localhost/fixture', getBackgroundThrottling: () => false
  }) as unknown as Electron.WebContents
  let surface: { width: number; height: number } | null = null
  const setSurface = vi.fn((size: typeof surface) => { surface = size })
  const diagnostics: string[] = []
  const calls: Array<{ method: string; params?: Record<string, unknown> }> = []
  const context: BrowserScreenshotContext = {
    tabId: 'tab', page, layoutSize: { width: 800, height: 600 }, visible: options.visible ?? true,
    timeoutMs: 10_000, setSurface,
    diagnostic: message => diagnostics.push(message),
    send: async (method, params) => {
      calls.push({ method, params })
      if (method === 'Page.getLayoutMetrics') return {
        cssContentSize: { x: 0, y: 0, width: 800, height: 2400 },
        cssVisualViewport: {
          pageX: 10, pageY: 20,
          clientWidth: surface?.width ?? context.layoutSize.width, clientHeight: surface?.height ?? context.layoutSize.height
        }
      }
      if (method === 'Runtime.evaluate') return { result: { value: 'ratio' in options ? options.ratio : 2 } }
      if (method === 'Page.captureScreenshot') return { data: 'capture-jpeg' }
      if (method === 'Page.startScreencast') debuggerApi.emit('message', {}, 'Page.screencastVisibilityChanged', { visible: false })
      return {}
    }
  }
  return {
    capture, context, page, debuggerApi, setSurface, diagnostics, calls, getSurface: () => surface,
    commands: () => calls.map(call => call.method),
    paramsOf: (method: string) => calls.filter(call => call.method === method).map(call => call.params)
  }
}

type Fixture = ReturnType<typeof fixture>

function emitFrames(f: Fixture, frames: Array<{ sessionId: number; data: string; age: number }>) {
  const original = f.context.send
  f.context.send = async (method, params) => {
    if (method === 'Page.startScreencast') {
      for (const frame of frames) {
        f.debuggerApi.emit('message', {}, 'Page.screencastFrame', {
          sessionId: frame.sessionId, data: frame.data, metadata: { timestamp: Date.now() / 1000 - frame.age }
        })
      }
      f.calls.push({ method, params })
      return {}
    }
    return await original(method, params)
  }
}

afterEach(() => vi.useRealTimers())

it('captures a visible tab in place from a fresh frame, acknowledging stale frames', async () => {
  const f = fixture()
  const acknowledgements: number[] = []
  const forwarded: string[] = []
  f.debuggerApi.on('message', (_event, method, params) => {
    if (!f.capture.consumesEvent(f.page, method, params)) forwarded.push(method)
  })
  emitFrames(f, [{ sessionId: 1, data: 'old', age: 10 }, { sessionId: 2, data: 'new-jpeg', age: 0 }])
  const send = f.context.send
  f.context.send = async (method, params) => {
    if (method === 'Page.screencastFrameAck') acknowledgements.push(Number(params?.sessionId))
    return await send(method, params)
  }
  expect(await f.capture.screenshot(f.context)).toBe('new-jpeg')
  expect(f.paramsOf('Page.startScreencast')).toEqual([
    { format: 'jpeg', quality: 80, everyNthFrame: 1, maxWidth: 800, maxHeight: 600 }
  ])
  expect(acknowledgements).toEqual([1, 2])
  expect(forwarded).toEqual([])
  expect(f.commands()).toContain('Page.stopScreencast')
  expect(f.commands()).not.toContain('Page.captureScreenshot')
  expect(f.setSurface).not.toHaveBeenCalled()
  expect(f.page.listenerCount('destroyed')).toBe(0)
  expect(f.debuggerApi.listenerCount('message')).toBe(1)
})

it('falls back to a CSS-pixel JPEG capture of the visual viewport after the bounded frame wait', async () => {
  vi.useFakeTimers()
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => method === 'Page.startScreencast' ? {} : await original(method, params)
  const result = f.capture.screenshot(f.context)
  await vi.advanceTimersByTimeAsync(2_000)
  expect(await result).toBe('capture-jpeg')
  expect(f.commands()).toContain('Page.stopScreencast')
  expect(f.paramsOf('Runtime.evaluate')).toEqual([{ expression: 'window.devicePixelRatio', returnByValue: true }])
  expect(f.paramsOf('Page.captureScreenshot')).toEqual([
    { format: 'jpeg', quality: 80, clip: { x: 10, y: 20, width: 800, height: 600, scale: 0.5 } }
  ])
  expect(f.setSurface).not.toHaveBeenCalled()
})

it('presents a hidden tab at its current layout size for the whole capture and releases it', async () => {
  const f = fixture({ visible: false })
  f.context.layoutSize = { width: 390, height: 844 }
  const surfaceAt: Record<string, unknown> = {}
  const original = f.context.send
  f.context.send = async (method, params) => {
    surfaceAt[method] = f.getSurface()
    return await original(method, params)
  }
  expect(await f.capture.screenshot(f.context)).toBe('capture-jpeg')
  expect(surfaceAt['Page.getLayoutMetrics']).toEqual({ width: 390, height: 844 })
  expect(surfaceAt['Page.startScreencast']).toEqual({ width: 390, height: 844 })
  expect(surfaceAt['Page.captureScreenshot']).toEqual({ width: 390, height: 844 })
  expect(f.setSurface.mock.calls).toEqual([[{ width: 390, height: 844 }], [null]])
})

it('skips the frame stream when the device ratio is below one', async () => {
  const f = fixture({ ratio: 0.5 })
  expect(await f.capture.screenshot(f.context)).toBe('capture-jpeg')
  expect(f.commands()).not.toContain('Page.startScreencast')
  expect(f.paramsOf('Page.captureScreenshot')).toEqual([
    { format: 'jpeg', quality: 80, clip: { x: 10, y: 20, width: 800, height: 600, scale: 2 } }
  ])
})

it.each([['unavailable', undefined], ['not a number', 'ratio']])('captures at scale one when the device ratio is %s', async (_name, ratio) => {
  const f = fixture({ ratio })
  expect(await f.capture.screenshot(f.context)).toBe('capture-jpeg')
  expect(f.paramsOf('Page.captureScreenshot')).toEqual([
    { format: 'jpeg', quality: 80, clip: { x: 10, y: 20, width: 800, height: 600, scale: 1 } }
  ])
})

it('leaves a raw screencast running while taking a screenshot', async () => {
  const f = fixture()
  await f.capture.rawScreencast(f.context, 'Page.startScreencast', async () => ({}))
  expect(await f.capture.screenshot(f.context)).toBe('capture-jpeg')
  expect(f.commands()).not.toContain('Page.startScreencast')
  expect(f.commands()).not.toContain('Page.stopScreencast')
  expect(f.capture.consumesEvent(f.page, 'Page.screencastFrame', { sessionId: 8 })).toBe(false)
  await f.capture.rawScreencast(f.context, 'Page.stopScreencast', async () => ({}))
  await f.capture.screenshot(f.context)
  expect(f.commands()).toContain('Page.startScreencast')
})

it.each(['metrics', 'capture'])('reports the %s timeout, releases the surface, and ignores late results', async stage => {
  vi.useFakeTimers()
  const f = fixture({ visible: false })
  const original = f.context.send
  let complete!: (result: unknown) => void
  f.context.send = async (method, params) => {
    if (method === (stage === 'metrics' ? 'Page.getLayoutMetrics' : 'Page.captureScreenshot')) {
      return await new Promise(resolve => { complete = resolve })
    }
    return await original(method, params)
  }
  const result = expect(f.capture.screenshot(f.context)).rejects.toMatchObject({ data: { stage } })
  await vi.advanceTimersByTimeAsync(5_000)
  await result
  expect(f.setSurface).toHaveBeenCalledWith({ width: 800, height: 600 })
  expect(f.getSurface()).toBeNull()
  expect(f.debuggerApi.listenerCount('message')).toBe(0)
  const count = f.calls.length
  complete({ data: 'late' })
  await vi.advanceTimersByTimeAsync(1)
  expect(f.getSurface()).toBeNull()
  expect(f.calls).toHaveLength(count)
  f.context.send = original
  expect(await f.capture.screenshot(f.context)).toBe('capture-jpeg')
})

it.each(['cancel', 'close', 'detach'])('cleans up an active screencast and surface after %s', async reason => {
  const f = fixture({ visible: false })
  const abort = new AbortController()
  f.context.signal = abort.signal
  const original = f.context.send
  let started!: () => void
  const ready = new Promise<void>(resolve => { started = resolve })
  f.context.send = async (method, params) => {
    if (method === 'Page.startScreencast') { started(); return {} }
    return await original(method, params)
  }
  const result = expect(f.capture.screenshot(f.context)).rejects.toThrow(reason === 'cancel' ? 'cancelled' : 'PageClosed')
  await ready
  expect(f.getSurface()).toEqual({ width: 800, height: 600 })
  if (reason === 'cancel') abort.abort()
  else if (reason === 'close') f.page.emit('destroyed')
  else f.debuggerApi.emit('detach', {}, 'closed')
  await result
  expect(f.commands()).toContain('Page.stopScreencast')
  expect(f.getSurface()).toBeNull()
  expect(f.page.listenerCount('destroyed')).toBe(0)
  expect(f.debuggerApi.listenerCount('message')).toBe(0)
  expect(f.debuggerApi.listenerCount('detach')).toBe(0)
})

const beyond = { format: 'jpeg', quality: 80, captureBeyondViewport: true }

it.each(['full-page', 'clip', 'cdp'])('captures %s on a surface sized to the clip and releases it', async kind => {
  const f = fixture()
  const clip = { x: 0, y: 0, width: 800, height: 2400, scale: 1 }
  if (kind === 'cdp') expect(await f.capture.captureCdp(f.context, { ...beyond, clip })).toEqual({ data: 'capture-jpeg' })
  else expect(await f.capture.screenshot(f.context, kind === 'clip' ? { clip } : { fullPage: true })).toBe('capture-jpeg')
  expect(f.setSurface.mock.calls).toEqual([[{ width: 800, height: 2400 }], [null]])
  expect(f.paramsOf('Page.captureScreenshot')).toEqual([
    { ...beyond, clip: kind === 'cdp' ? clip : { ...clip, scale: 0.5 } }
  ])
  expect(f.commands()).not.toContain('Page.startScreencast')
  expect(f.diagnostics.join('\n')).not.toContain('capture-jpeg')
})

it.each([
  ['a visible tab without a clip', true, { format: 'png' }, null],
  ['a hidden tab without a clip', false, { format: 'png' }, { width: 800, height: 600 }],
  ['a visible tab with a clip but within the viewport', true, { clip: { x: 0, y: 0, width: 800, height: 2400, scale: 1 } }, null],
  ['a hidden tab with a clip but within the viewport', false, { clip: { x: 0, y: 0, width: 800, height: 2400, scale: 1 } }, { width: 800, height: 600 }],
  ['a visible tab beyond the viewport without a clip', true, { captureBeyondViewport: true }, null],
  ['a visible tab beyond the viewport with a non-numeric clip', true, { captureBeyondViewport: true, clip: { width: '800', height: 2400 } }, null]
])('passes a capture straight through for %s', async (_name, visible, params, surface) => {
  const f = fixture({ visible })
  expect(await f.capture.captureCdp(f.context, params)).toEqual({ data: 'capture-jpeg' })
  expect(f.paramsOf('Page.captureScreenshot')).toEqual([params])
  expect(f.setSurface.mock.calls).toEqual(surface ? [[surface], [null]] : [])
  expect(f.commands()).not.toContain('Runtime.evaluate')
})

it('bounds layout settling and still attempts capture when dimensions have not caught up', async () => {
  vi.useFakeTimers()
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => method === 'Page.getLayoutMetrics'
    ? { cssVisualViewport: { clientWidth: 800, clientHeight: 600 } }
    : await original(method, params)
  const result = f.capture.captureCdp(f.context, { ...beyond, clip: { x: 0, y: 0, width: 800, height: 2400 } })
  await vi.advanceTimersByTimeAsync(1_000)
  expect(await result).toEqual({ data: 'capture-jpeg' })
  expect(f.getSurface()).toBeNull()
})

it('restores a failed full-page capture and rejects an empty image', async () => {
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => method === 'Page.captureScreenshot' ? { data: '' } : await original(method, params)
  await expect(f.capture.screenshot(f.context, { fullPage: true })).rejects.toThrow('returned no data')
  expect(f.getSurface()).toBeNull()
})

it('serializes captures so an earlier cleanup cannot clear the next surface', async () => {
  const f = fixture()
  const original = f.context.send
  let release!: () => void
  let first = true
  let started!: () => void
  const ready = new Promise<void>(resolve => { started = resolve })
  f.context.send = async (method, params) => {
    if (method === 'Page.captureScreenshot' && first) {
      first = false
      started()
      await new Promise<void>(resolve => { release = resolve })
    }
    return await original(method, params)
  }
  const a = f.capture.screenshot(f.context, { fullPage: true })
  await ready
  f.context.visible = false
  const b = f.capture.screenshot(f.context)
  await Promise.resolve()
  expect(f.getSurface()).toEqual({ width: 800, height: 2400 })
  release()
  expect(await a).toBe('capture-jpeg')
  expect(await b).toBe('capture-jpeg')
  expect(f.setSurface.mock.calls).toEqual([
    [{ width: 800, height: 2400 }], [null], [{ width: 800, height: 600 }], [null]
  ])
})

it('does not run a queued capture after its request was cancelled', async () => {
  const f = fixture()
  const original = f.context.send
  let release!: () => void
  let started!: () => void
  const ready = new Promise<void>(resolve => { started = resolve })
  f.context.send = async (method, params) => {
    if (method === 'Page.captureScreenshot') {
      started()
      await new Promise<void>(resolve => { release = resolve })
    }
    return await original(method, params)
  }
  const first = f.capture.screenshot(f.context, { fullPage: true })
  await ready
  const abort = new AbortController()
  const cancelled = { ...f.context, visible: false, signal: abort.signal, setSurface: vi.fn() }
  const second = expect(f.capture.screenshot(cancelled)).rejects.toThrow('cancelled')
  abort.abort()
  expect(f.getSurface()).toEqual({ width: 800, height: 2400 })
  release()
  await first
  await second
  expect(cancelled.setSurface).not.toHaveBeenCalled()
})

it('does not resume layout polling when a cancelled command completes late', async () => {
  vi.useFakeTimers()
  const f = fixture()
  const abort = new AbortController()
  f.context.signal = abort.signal
  let complete!: (value: unknown) => void
  f.context.send = vi.fn(async () => await new Promise(resolve => { complete = resolve }))
  const result = expect(f.capture.captureCdp(f.context, {
    captureBeyondViewport: true, clip: { x: 0, y: 0, width: 800, height: 2400 }
  })).rejects.toThrow('cancelled')
  await vi.advanceTimersByTimeAsync(0)
  abort.abort()
  await result
  complete({ cssVisualViewport: { clientWidth: 800, clientHeight: 600 } })
  await vi.advanceTimersByTimeAsync(100)
  expect(f.context.send).toHaveBeenCalledTimes(1)
  expect(f.getSurface()).toBeNull()
})

it.each(['metrics', 'surface-wait', 'capture'])('identifies a rejected %s command without continuing capture', async stage => {
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => {
    if (method === (stage === 'capture' ? 'Page.captureScreenshot' : 'Page.getLayoutMetrics')) throw new Error('CDP disconnected')
    return await original(method, params)
  }
  const result = stage === 'surface-wait'
    ? f.capture.captureCdp(f.context, { captureBeyondViewport: true, clip: { x: 0, y: 0, width: 800, height: 2400 } })
    : f.capture.screenshot(f.context)
  await expect(result).rejects.toMatchObject({
    message: expect.stringContaining(`Screenshot ${stage} failed`), data: { stage }
  })
  if (stage !== 'capture') expect(f.commands()).not.toContain('Page.captureScreenshot')
  expect(f.getSurface()).toBeNull()
})

it('keeps the current layout size when a crop is smaller than the viewport', async () => {
  const f = fixture()
  const surfaces: Array<{ width: number; height: number } | null> = []
  f.setSurface.mockImplementation(size => { surfaces.push(size) })
  await f.capture.screenshot(f.context, { clip: { x: 0, y: 0, width: 100, height: 100 } })
  expect(surfaces[0]).toEqual({ width: 800, height: 600 })
})
