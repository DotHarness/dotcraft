import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { WebContents } from 'electron'
import { viewerTextWatchManager } from '../viewerTextWatch'

const fsMock = vi.hoisted(() => ({ watch: vi.fn(), stat: vi.fn() }))
vi.mock('fs', () => ({ watch: fsMock.watch, promises: { stat: fsMock.stat } }))

describe('viewer text subscriptions', () => {
  let watcher: EventEmitter & { close: ReturnType<typeof vi.fn> }
  let sender: EventEmitter & {
    id: number
    send: ReturnType<typeof vi.fn>
    isDestroyed: ReturnType<typeof vi.fn>
  }
  let notify: (event: string, name: string) => void
  beforeEach(() => {
    vi.useFakeTimers()
    watcher = Object.assign(new EventEmitter(), { close: vi.fn() })
    sender = Object.assign(new EventEmitter(), {
      id: 1,
      send: vi.fn(),
      isDestroyed: vi.fn(() => false)
    })
    fsMock.watch.mockImplementation((_path, _options, callback) => {
      notify = callback
      return watcher
    })
    fsMock.stat.mockResolvedValue({ isFile: () => true, mtimeMs: 4, size: 12 })
  })
  afterEach(() => {
    viewerTextWatchManager.disposeAll()
    vi.useRealTimers()
    vi.clearAllMocks()
  })

  it('coalesces matching-file events and isolates unsubscribe by sender', async () => {
    const id = viewerTextWatchManager.subscribe(
      sender as unknown as WebContents,
      'C:/repo/notes.txt'
    )
    notify('change', 'unrelated.txt')
    await vi.advanceTimersByTimeAsync(100)
    expect(sender.send).not.toHaveBeenCalled()
    notify('change', 'notes.txt')
    notify('rename', 'notes.txt')
    viewerTextWatchManager.unsubscribe(id, 2)
    await vi.advanceTimersByTimeAsync(80)
    expect(sender.send).toHaveBeenCalledExactlyOnceWith(
      'workspace:viewer:text-changed',
      expect.objectContaining({ subscriptionId: id, mtimeMs: 4, sizeBytes: 12 })
    )
    viewerTextWatchManager.unsubscribe(id, 1)
    expect(watcher.close).toHaveBeenCalledOnce()
    expect(sender.listenerCount('destroyed')).toBe(0)
  })

  it('cancels pending events when the renderer is destroyed', async () => {
    viewerTextWatchManager.subscribe(sender as unknown as WebContents, 'C:/repo/notes.txt')
    notify('change', 'notes.txt')
    sender.emit('destroyed')
    await vi.advanceTimersByTimeAsync(100)
    expect(watcher.close).toHaveBeenCalledOnce()
    expect(sender.send).not.toHaveBeenCalled()
  })

  it('handles watcher errors without an uncaught main-process exception', () => {
    viewerTextWatchManager.subscribe(sender as unknown as WebContents, 'C:/repo/notes.txt')
    expect(() => watcher.emit('error', new Error('watch unavailable'))).not.toThrow()
    expect(watcher.close).toHaveBeenCalledOnce()
    expect(sender.listenerCount('destroyed')).toBe(0)
  })
})
