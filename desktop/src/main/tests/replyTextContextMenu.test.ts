import { EventEmitter } from 'node:events'
import type { MenuItemConstructorOptions, WebContents } from 'electron'
import { beforeEach, expect, it, vi } from 'vitest'
import { showReplyTextContextMenu } from '../replyTextContextMenu'

const native = vi.hoisted(() => ({ build: vi.fn(), from: vi.fn(), popup: vi.fn() }))
vi.mock('electron', () => ({
  BrowserWindow: { fromWebContents: native.from },
  Menu: { buildFromTemplate: native.build },
}))

beforeEach(() => {
  vi.clearAllMocks()
  native.build.mockReturnValue({ popup: native.popup })
})

function setup(selectionText = 'A & 中文\nB') {
  const sender = Object.assign(new EventEmitter(), { isDestroyed: () => false, copy: vi.fn(), selectAll: vi.fn() })
  const window = { webContents: sender, isDestroyed: () => false }
  native.from.mockReturnValue(window)
  const open = vi.fn(async (_url: string) => {})
  const completion = showReplyTextContextMenu(sender as unknown as WebContents,
    { x: 20, y: 30, selectionText }, 'en', open)
  const items = native.build.mock.calls[0][0] as MenuItemConstructorOptions[]
  return { sender, window, open, completion, items, close: () => native.popup.mock.calls[0][0].callback() }
}

it('searches the exact selection in the external browser and completes after dismissal', async () => {
  const { items, open, completion, close, sender, window } = setup()
  const finished = vi.fn()
  void completion.then(finished)
  items[0].click?.(undefined as never, undefined as never, undefined as never)
  expect(new URL(open.mock.calls[0][0]).searchParams.get('q')).toBe('A & 中文\nB')
  expect(new URL(open.mock.calls[0][0]).origin).toBe('https://www.google.com')
  expect(native.popup).toHaveBeenCalledWith(expect.objectContaining({ window, x: 20, y: 30 }))
  await Promise.resolve()
  expect(finished).not.toHaveBeenCalled()
  close()
  await completion
  expect(sender.listenerCount('destroyed')).toBe(0)
})

it('copies and selects using only the requesting WebContents', async () => {
  const { sender, items, completion, close } = setup()
  items.find((item) => item.accelerator === 'CmdOrCtrl+C')!.click?.(undefined as never, undefined as never, undefined as never)
  expect(sender.copy).toHaveBeenCalledOnce()
  if (process.platform !== 'darwin') {
    items.at(-1)!.click?.(undefined as never, undefined as never, undefined as never)
    expect(sender.selectAll).toHaveBeenCalledOnce()
  }
  close()
  await completion
})

it.skipIf(process.platform === 'darwin')('offers only native select-all without a selection', async () => {
  const { sender, items, open, completion, close } = setup('')
  expect(items).toHaveLength(1)
  items[0].click?.(undefined as never, undefined as never, undefined as never)
  expect(sender.selectAll).toHaveBeenCalledOnce()
  expect(sender.copy).not.toHaveBeenCalled()
  expect(open).not.toHaveBeenCalled()
  close()
  await completion
})

it('rejects embedded-page senders and invalid requests', async () => {
  const sender = { isDestroyed: () => false } as WebContents
  native.from.mockReturnValue({ webContents: {}, isDestroyed: () => false })
  await showReplyTextContextMenu(sender, { x: 0, y: 0, selectionText: 'text' }, 'en', vi.fn())
  expect(native.build).not.toHaveBeenCalled()
  await expect(showReplyTextContextMenu(sender, { x: NaN, y: 0, selectionText: '' }, 'en', vi.fn())).rejects.toThrow()
})

it('settles when its source closes and propagates external-open failures', async () => {
  const { items, open, completion, close } = setup()
  open.mockRejectedValueOnce(new Error('Open failed'))
  items[0].click?.(undefined as never, undefined as never, undefined as never)
  close()
  await expect(completion).rejects.toThrow('Open failed')
  const next = setup()
  next.sender.emit('destroyed')
  await next.completion
  expect(next.sender.listenerCount('destroyed')).toBe(0)
  native.popup.mockImplementationOnce(() => { throw new Error('Popup failed') })
  const failed = setup()
  await expect(failed.completion).rejects.toThrow('Popup failed')
  expect(failed.sender.listenerCount('destroyed')).toBe(0)
})
