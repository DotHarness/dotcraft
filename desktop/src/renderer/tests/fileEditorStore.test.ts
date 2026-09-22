// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { useFileEditorStore } from '../stores/fileEditorStore'
import type { ReadTextResult, WriteTextParams } from '../../shared/viewer/types'

const opened: ReadTextResult = {
  text: 'base\n',
  truncated: false,
  encoding: 'utf-8',
  sizeBytes: 5,
  mtimeMs: 10,
  hasUtf8Bom: false,
  lineEnding: 'lf'
}
const store = () => useFileEditorStore.getState()
const session = () => store().sessions.get('tab')!
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}

describe('file editor sessions', () => {
  let disk: ReadTextResult
  let changed: () => void
  const stop = vi.fn()
  const readText = vi.fn()
  const writeText = vi.fn()
  const write = async (request: WriteTextParams) => {
    if (request.expectedMtimeMs !== disk.mtimeMs)
      return { outcome: 'conflict', current: { ...disk } }
    disk = {
      ...disk,
      text: request.text,
      mtimeMs: disk.mtimeMs + 1,
      sizeBytes: request.text.length
    }
    return { outcome: 'saved', mtimeMs: disk.mtimeMs, sizeBytes: disk.sizeBytes }
  }
  const open = async (markdown = false) =>
    store().load('tab', 'C:/repo/file.' + (markdown ? 'md' : 'cs'), markdown)
  const external = async (text: string) => {
    disk = { ...disk, text, mtimeMs: disk.mtimeMs + 1 }
    await store().refreshFromDisk('tab')
  }
  beforeEach(() => {
    vi.useFakeTimers()
    disk = { ...opened }
    readText.mockReset().mockImplementation(async () => ({ ...disk }))
    writeText.mockReset().mockImplementation(write)
    stop.mockReset()
    installDesktopApiMock({
      workspace: {
        viewer: {
          readText,
          writeText,
          watchText: vi.fn(async (_request, callback) => {
            changed = callback
            return stop
          })
        }
      }
    })
  })
  afterEach(() => {
    for (const id of store().sessions.keys()) store().discard(id)
    vi.useRealTimers()
  })

  it('debounces for three seconds without a mounted component', async () => {
    await open()
    store().updateText('tab', 'first')
    await vi.advanceTimersByTimeAsync(2000)
    store().updateText('tab', 'second')
    await vi.advanceTimersByTimeAsync(2999)
    expect(writeText).not.toHaveBeenCalled()
    await vi.advanceTimersByTimeAsync(1)
    expect(disk.text).toBe('second')
    expect(session().baseText).toBe('second')
  })
  it('closes an unchanged loading file without a discard warning and ignores its late result', async () => {
    const gate = deferred<ReadTextResult>()
    readText.mockReturnValueOnce(gate.promise)
    const loading = open()
    await expect(store().save('tab')).resolves.toBe(true)
    store().discard('tab')
    gate.resolve(opened)
    await loading
    expect(store().sessions.has('tab')).toBe(false)
    expect(writeText).not.toHaveBeenCalled()
  })
  it('shares pending saves and saves input arriving during a write before closing', async () => {
    await open()
    const gate = deferred<void>()
    writeText.mockImplementationOnce(async (request) => {
      await gate.promise
      return write(request)
    })
    store().updateText('tab', 'first')
    const saving = store().save('tab')
    await vi.advanceTimersByTimeAsync(0)
    store().updateText('tab', 'last input')
    const closing = store().save('tab')
    expect(writeText).toHaveBeenCalledTimes(1)
    gate.resolve()
    await expect(saving).resolves.toBe(true)
    await expect(closing).resolves.toBe(true)
    expect(writeText).toHaveBeenCalledTimes(2)
    expect(disk.text).toBe('last input')
  })
  it('retries immediately on Save, and schedules retries after more input', async () => {
    await open()
    writeText.mockRejectedValueOnce(new Error('disk full'))
    store().updateText('tab', 'draft')
    await expect(store().save('tab')).resolves.toBe(false)
    expect(session().saveState).toBe('failed')
    await expect(store().save('tab')).resolves.toBe(true)
    writeText.mockRejectedValueOnce(new Error('again'))
    store().updateText('tab', 'second')
    await store().save('tab')
    store().updateText('tab', 'third')
    await vi.advanceTimersByTimeAsync(3000)
    expect(disk.text).toBe('third')
  })
  it('does not retry a failed save endlessly without a new edit', async () => {
    await open()
    writeText.mockRejectedValue(new Error('denied'))
    store().updateText('tab', 'draft')
    await vi.advanceTimersByTimeAsync(10000)
    expect(writeText).toHaveBeenCalledTimes(1)
    expect(session().text).toBe('draft')
  })
  it('keeps mode on failure and switches only after the last input is saved', async () => {
    await open(true)
    store().updateText('tab', '# Draft')
    writeText.mockRejectedValueOnce(new Error('denied'))
    await expect(store().switchMode('tab', 'source')).resolves.toBe(false)
    expect(session().mode).toBe('preview')
    const gate = deferred<void>()
    writeText.mockImplementationOnce(async (request) => {
      await gate.promise
      return write(request)
    })
    const switching = store().switchMode('tab', 'source')
    await vi.advanceTimersByTimeAsync(0)
    store().updateText('tab', '# Last')
    gate.resolve()
    await expect(switching).resolves.toBe(true)
    expect(session().mode).toBe('source')
    expect(disk.text).toBe('# Last')
  })
  it('updates metadata without interrupting local editing when disk equals baseline', async () => {
    await open()
    store().updateText('tab', 'local')
    await external('base\n')
    expect(session()).toMatchObject({
      text: 'local',
      baseText: 'base\n',
      mtimeMs: 11,
      saveState: 'idle'
    })
    expect(session().review).toBeUndefined()
  })
  it('cleans an externally saved identical draft', async () => {
    await open()
    store().updateText('tab', 'shared')
    await external('shared')
    expect(session()).toMatchObject({ text: 'shared', baseText: 'shared', saveState: 'idle' })
    await vi.advanceTimersByTimeAsync(3000)
    expect(writeText).not.toHaveBeenCalled()
  })
  it('reviews clean source changes and auto-accepts clean semantic Markdown', async () => {
    await open()
    await external('disk')
    expect(session().review).toMatchObject({ oldText: 'base\n', newText: 'disk' })
    expect(session().text).toBe('base\n')
    store().discard('tab')
    await open(true)
    await external('markdown disk')
    expect(session().text).toBe('markdown disk')
    expect(session().review).toBeUndefined()
    await store().switchMode('tab', 'source')
    await external('source disk')
    expect(session().review?.newText).toBe('source disk')
  })
  it.each(['accept', 'reject', 'edit'] as const)(
    'resolves clean source review with %s using old/new roles',
    async (choice) => {
      await open()
      await external('disk')
      await expect(store().resolveReview('tab', choice)).resolves.toBe(true)
      expect(session().baseText).toBe('disk')
      expect(session().text).toBe(choice === 'reject' ? 'base\n' : 'disk')
      expect(session().focusRevision).toBe(choice === 'edit' ? 1 : 0)
      await vi.advanceTimersByTimeAsync(3000)
      expect(disk.text).toBe(choice === 'reject' ? 'base\n' : 'disk')
    }
  )
  it.each(['accept', 'reject', 'edit'] as const)(
    'resolves divergent review with %s using old/new roles',
    async (choice) => {
      await open()
      store().updateText('tab', 'local')
      await external('disk')
      expect(session().review).toMatchObject({ oldText: 'disk', newText: 'local' })
      await store().resolveReview('tab', choice)
      expect(session().baseText).toBe('disk')
      expect(session().text).toBe(choice === 'reject' ? 'disk' : 'local')
      await vi.advanceTimersByTimeAsync(3000)
      expect(disk.text).toBe(choice === 'reject' ? 'disk' : 'local')
    }
  )
  it('blocks saving, mode switching and closing during unresolved review', async () => {
    await open(true)
    store().updateText('tab', 'local')
    await external('disk')
    await expect(store().switchMode('tab', 'source')).resolves.toBe(false)
    await expect(store().save('tab')).resolves.toBe(false)
    await vi.advanceTimersByTimeAsync(6000)
    expect(writeText).not.toHaveBeenCalled()
    expect(session().mode).toBe('preview')
  })
  it('revalidates review against disk before applying a decision', async () => {
    await open()
    await external('disk one')
    disk = { ...disk, text: 'disk two', mtimeMs: 12 }
    await expect(store().resolveReview('tab', 'accept')).resolves.toBe(false)
    expect(session().review?.newText).toBe('disk two')
    await expect(store().resolveReview('tab', 'accept')).resolves.toBe(true)
    expect(session().text).toBe('disk two')
  })
  it('handles write conflicts without overwriting the competing version', async () => {
    await open()
    store().updateText('tab', 'local')
    disk = { ...disk, text: 'other', mtimeMs: 15 }
    await expect(store().save('tab')).resolves.toBe(false)
    expect(disk.text).toBe('other')
    expect(session().review).toMatchObject({ oldText: 'other', newText: 'local' })
  })
  it('keeps read-only Markdown in preview and permits a review decision after disk growth', async () => {
    disk = { ...disk, readOnlyReason: 'large-file', sizeBytes: 11 * 1024 * 1024 }
    await open(true)
    expect(session().mode).toBe('preview')
    store().updateText('tab', 'not allowed')
    expect(session().text).toBe('base\n')
    store().discard('tab')
    disk = { ...opened }
    await open()
    store().updateText('tab', 'local')
    disk = {
      ...disk,
      text: 'large disk',
      readOnlyReason: 'large-file',
      sizeBytes: 11 * 1024 * 1024,
      mtimeMs: 20
    }
    await store().refreshFromDisk('tab')
    await store().resolveReview('tab', 'accept')
    await expect(store().save('tab')).resolves.toBe(true)
    expect(disk.text).toBe('local')
  })
  it('owns subscriptions until discard and ignores late reads from disposed sessions', async () => {
    await open()
    disk = { ...disk, text: 'changed', mtimeMs: 30 }
    changed()
    await vi.advanceTimersByTimeAsync(0)
    expect(session().review?.newText).toBe('changed')
    const gate = deferred<ReadTextResult>()
    readText.mockReturnValueOnce(gate.promise)
    const refresh = store().refreshFromDisk('tab')
    await vi.advanceTimersByTimeAsync(0)
    store().discard('tab')
    gate.resolve({ ...disk, text: 'late' })
    await refresh
    expect(store().sessions.has('tab')).toBe(false)
    expect(stop).toHaveBeenCalledTimes(1)
  })
  it('reconciles a disk change between initial reading and subscription registration', async () => {
    const subscribing = deferred<() => void>()
    vi.mocked(window.api.workspace.viewer.watchText).mockReturnValueOnce(subscribing.promise)
    await open()
    disk = { ...disk, text: 'changed before watch', mtimeMs: 30 }
    subscribing.resolve(stop)
    await vi.advanceTimersByTimeAsync(0)
    expect(session().review?.newText).toBe('changed before watch')
  })
})
