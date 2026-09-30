import { EventEmitter } from 'node:events'
import { afterEach, expect, it, vi } from 'vitest'
import { BrowserScreenshot, type BrowserScreenshotContext } from '../browserScreenshot'

function fixture() {
  const capture = new BrowserScreenshot()
  const debuggerApi = new EventEmitter()
  const page = Object.assign(new EventEmitter(), {
    debugger: debuggerApi, isDestroyed: () => false, getURL: () => 'http://localhost/fixture', getBackgroundThrottling: () => false
  }) as unknown as Electron.WebContents
  let surface: { width: number; height: number } | null = null
  const viewport = { width: 800, height: 600 }
  const setSurface = vi.fn((size: typeof surface) => { surface = size })
  const diagnostics: string[] = []
  const commands: string[] = []
  const context: BrowserScreenshotContext = {
    tabId: 'tab', page, viewport, timeoutMs: 10_000, setSurface,
    diagnostic: message => diagnostics.push(message),
    send: async method => {
      commands.push(method)
      if (method === 'Page.getLayoutMetrics') return {
        cssContentSize: { x: 0, y: 0, width: 800, height: 2400 },
        cssVisualViewport: { pageX: 0, pageY: 0, clientWidth: surface?.width ?? 800, clientHeight: surface?.height ?? 600 }
      }
      if (method === 'Page.captureScreenshot') return { data: 'capture-png' }
      if (method === 'Page.startScreencast') debuggerApi.emit('message', {}, 'Page.screencastVisibilityChanged', { visible: false })
      return {}
    }
  }
  return { capture, context, page, debuggerApi, setSurface, diagnostics, commands, getSurface: () => surface }
}

afterEach(() => vi.useRealTimers())

it('returns a new frame, acknowledges stale frames, and cleans up without leaking internal events', async () => {
  const f = fixture()
  const original = f.context.send
  const acknowledgements: number[] = []
  const forwarded: string[] = []
  f.debuggerApi.on('message', (_event, method, params) => {
    if (!f.capture.consumesEvent(f.page, method, params)) forwarded.push(method)
  })
  f.context.send = async (method, params) => {
    if (method === 'Page.startScreencast') {
      f.debuggerApi.emit('message', {}, 'Page.screencastFrame', { sessionId: 1, data: 'old', metadata: { timestamp: Date.now() / 1000 - 10 } })
      f.debuggerApi.emit('message', {}, 'Page.screencastFrame', { sessionId: 2, data: 'new-png', metadata: { timestamp: Date.now() / 1000 } })
      return {}
    }
    if (method === 'Page.screencastFrameAck') acknowledgements.push(Number(params?.sessionId))
    return await original(method, params)
  }
  expect(await f.capture.screenshot(f.context)).toBe('new-png')
  expect(acknowledgements).toEqual([1, 2])
  expect(forwarded).toEqual([])
  expect(f.commands).toContain('Page.stopScreencast')
  expect(f.commands).not.toContain('Page.captureScreenshot')
  expect(f.getSurface()).toBeNull()
  expect(f.page.listenerCount('destroyed')).toBe(0)
  expect(f.debuggerApi.listenerCount('message')).toBe(1)
})

it('falls back after the bounded screencast wait and stops the stream', async () => {
  vi.useFakeTimers()
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => method === 'Page.startScreencast' ? {} : await original(method, params)
  const result = f.capture.screenshot(f.context)
  await vi.advanceTimersByTimeAsync(2_000)
  expect(await result).toBe('capture-png')
  expect(f.commands).toContain('Page.stopScreencast')
  expect(f.commands).toContain('Page.captureScreenshot')
  expect(f.getSurface()).toBeNull()
})

it('leaves a raw screencast running while taking a screenshot', async () => {
  const f = fixture()
  await f.capture.rawScreencast(f.context, 'Page.startScreencast', async () => ({}))
  expect(await f.capture.screenshot(f.context)).toBe('capture-png')
  expect(f.commands).not.toContain('Page.startScreencast')
  expect(f.commands).not.toContain('Page.stopScreencast')
  expect(f.capture.consumesEvent(f.page, 'Page.screencastFrame', { sessionId: 8 })).toBe(false)
  await f.capture.rawScreencast(f.context, 'Page.stopScreencast', async () => ({}))
  await f.capture.screenshot(f.context)
  expect(f.commands).toContain('Page.startScreencast')
})

it.each(['metrics', 'capture'])('reports the %s timeout, releases the surface, and ignores late results', async stage => {
  vi.useFakeTimers()
  const f = fixture()
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
  expect(f.getSurface()).toBeNull()
  expect(f.debuggerApi.listenerCount('message')).toBe(0)
  const count = f.commands.length
  complete({ data: 'late' })
  await vi.advanceTimersByTimeAsync(1)
  expect(f.getSurface()).toBeNull()
  expect(f.commands).toHaveLength(count)
  f.context.send = original
  expect(await f.capture.screenshot(f.context)).toBe('capture-png')
})

it.each(['cancel', 'close', 'detach'])('cleans up an active screencast after %s', async reason => {
  const f = fixture()
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
  if (reason === 'cancel') abort.abort()
  else if (reason === 'close') f.page.emit('destroyed')
  else f.debuggerApi.emit('detach', {}, 'closed')
  await result
  expect(f.commands).toContain('Page.stopScreencast')
  expect(f.getSurface()).toBeNull()
  expect(f.page.listenerCount('destroyed')).toBe(0)
  expect(f.debuggerApi.listenerCount('message')).toBe(0)
  expect(f.debuggerApi.listenerCount('detach')).toBe(0)
})

it.each(['full-page', 'clip', 'cdp'])('uses and restores a temporary surface for %s captures', async kind => {
  const f = fixture()
  const params = { format: 'png', captureBeyondViewport: true, clip: { x: 0, y: 0, width: 800, height: 2400, scale: 1 } }
  if (kind === 'cdp') expect(await f.capture.captureCdp(f.context, params)).toEqual({ data: 'capture-png' })
  else expect(await f.capture.screenshot(f.context, kind === 'clip' ? { clip: params.clip } : { fullPage: true })).toBe('capture-png')
  expect(f.setSurface).toHaveBeenCalledWith({ width: 800, height: 2400 })
  expect(f.context.viewport).toEqual({ width: 800, height: 600 })
  expect(f.getSurface()).toBeNull()
  expect(f.commands).not.toContain('Page.startScreencast')
  expect(f.diagnostics.join('\n')).not.toContain('capture-png')
})

it('bounds layout settling and still attempts capture when dimensions have not caught up', async () => {
  vi.useFakeTimers()
  const f = fixture()
  const original = f.context.send
  f.context.send = async (method, params) => method === 'Page.getLayoutMetrics'
    ? { cssVisualViewport: { clientWidth: 800, clientHeight: 600 } }
    : await original(method, params)
  const result = f.capture.captureCdp(f.context, { format: 'png', captureBeyondViewport: true, clip: { x: 0, y: 0, width: 800, height: 2400 } })
  await vi.advanceTimersByTimeAsync(1_000)
  expect(await result).toEqual({ data: 'capture-png' })
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
  const b = f.capture.screenshot(f.context)
  await Promise.resolve()
  expect(f.getSurface()).toEqual({ width: 800, height: 2400 })
  release()
  expect(await a).toBe('capture-png')
  expect(await b).toBe('capture-png')
  expect(f.getSurface()).toBeNull()
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
  const cancelled = { ...f.context, signal: abort.signal, setSurface: vi.fn() }
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
  if (stage !== 'capture') expect(f.commands).not.toContain('Page.captureScreenshot')
  expect(f.getSurface()).toBeNull()
})
