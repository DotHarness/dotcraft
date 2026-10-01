import { afterEach, expect, it, vi } from 'vitest'
import type { BrowserWindow } from 'electron'
import type { BrowserHostDescriptor } from '../../shared/viewer/browserHost'
import { BrowserAutomationCursors, CURSOR_ARRIVAL_TIMEOUT_MS } from '../browserAutomationCursor'

function fixture() {
  const hosts = new Map<string, BrowserHostDescriptor>()
  const key = (win: BrowserWindow, tabId: string) => `${win.id}:${tabId}`
  const cursors = new BrowserAutomationCursors({
    get: (win, tabId) => hosts.get(key(win, tabId)),
    update: (win, tabId, changes) => {
      const current = hosts.get(key(win, tabId))
      if (current) hosts.set(key(win, tabId), { ...current, ...changes })
    }
  })
  const focus = { value: true }
  const win = { id: 1, isFocused: () => focus.value } as unknown as BrowserWindow
  const otherWin = { id: 2, isFocused: () => true } as unknown as BrowserWindow
  function add(owner: BrowserWindow, tabId: string, changes: Partial<BrowserHostDescriptor> = {}): void {
    hosts.set(key(owner, tabId), {
      tabId, partition: 'persist:test', visible: true, automation: true,
      bounds: { x: 0, y: 0, width: 800, height: 600 }, ...changes
    })
  }
  add(win, 'tab')
  add(otherWin, 'tab')
  const cursorOf = (owner: BrowserWindow = win, tabId = 'tab') => hosts.get(key(owner, tabId))?.cursor
  return { cursors, win, otherWin, focus, hosts, key, add, cursorOf }
}

async function settled(promise: Promise<void>): Promise<boolean> {
  let done = false
  void promise.then(() => { done = true })
  await Promise.resolve()
  await Promise.resolve()
  return done
}

afterEach(() => vi.useRealTimers())

it('shows an activated cursor at rest, hides it on deactivation and forgets the position on reactivation', async () => {
  const { cursors, win, cursorOf } = fixture()
  expect(cursorOf()).toBeUndefined()
  cursors.activate(win, 'tab')
  expect(cursorOf()).toEqual({ visible: true })
  await cursors.move(win, 'tab', { x: 120, y: 80 }, { waitForArrival: false })
  cursors.activate(win, 'tab')
  expect(cursorOf()).toMatchObject({ visible: true, x: 120, y: 80 })
  cursors.deactivate(win, 'tab')
  expect(cursorOf()).toEqual({ visible: false, x: 120, y: 80 })
  cursors.activate(win, 'tab')
  expect(cursorOf()).toEqual({ visible: true })
})

it('clears the cursor state on release', async () => {
  const { cursors, win, cursorOf } = fixture()
  cursors.activate(win, 'tab')
  await cursors.move(win, 'tab', { x: 5, y: 6 }, { waitForArrival: false })
  cursors.release(win, 'tab')
  expect(cursorOf()).toBeUndefined()
})

it('ignores moves for tabs that are not automation-active', async () => {
  const { cursors, win, cursorOf } = fixture()
  await cursors.move(win, 'tab', { x: 5, y: 6 })
  expect(cursorOf()).toBeUndefined()
  cursors.activate(win, 'tab')
  cursors.deactivate(win, 'tab')
  await cursors.move(win, 'tab', { x: 5, y: 6 })
  expect(cursorOf()).toMatchObject({ visible: false })
  expect(cursorOf()!.moveSequence).toBeUndefined()
})

it('assigns increasing sequences and waits for the matching arrival acknowledgement', async () => {
  const { cursors, win, cursorOf } = fixture()
  cursors.activate(win, 'tab')
  const first = cursors.move(win, 'tab', { x: 10, y: 20 })
  const firstSequence = cursorOf()!.moveSequence!
  expect(cursorOf()).toMatchObject({ visible: true, x: 10, y: 20, animate: true })
  expect(await settled(first)).toBe(false)
  cursors.arrived(win, 'tab', firstSequence)
  await first

  const second = cursors.move(win, 'tab', { x: 30, y: 40 })
  expect(cursorOf()!.moveSequence).toBeGreaterThan(firstSequence)
  cursors.arrived(win, 'tab', cursorOf()!.moveSequence!)
  await second
})

it.each([
  ['the window is not focused', (ctx: ReturnType<typeof fixture>) => { ctx.focus.value = false }],
  ['the tab is not visible', (ctx: ReturnType<typeof fixture>) => ctx.add(ctx.win, 'tab', { visible: false, cursor: { visible: true } })],
  ['the tab is presented on a capture surface', (ctx: ReturnType<typeof fixture>) => ctx.add(ctx.win, 'tab', { captureSurfaceSize: { width: 800, height: 2000 }, cursor: { visible: true } })]
])('snaps without waiting when %s', async (_name, arrange) => {
  vi.useFakeTimers()
  const ctx = fixture()
  ctx.cursors.activate(ctx.win, 'tab')
  arrange(ctx)
  await ctx.cursors.move(ctx.win, 'tab', { x: 10, y: 20 })
  expect(vi.getTimerCount()).toBe(0)
  expect(ctx.cursorOf()).toMatchObject({ visible: true, x: 10, y: 20, animate: false })
})

it('does not wait when waitForArrival is false', async () => {
  vi.useFakeTimers()
  const { cursors, win, cursorOf } = fixture()
  cursors.activate(win, 'tab')
  await cursors.move(win, 'tab', { x: 10, y: 20 }, { waitForArrival: false })
  expect(vi.getTimerCount()).toBe(0)
  expect(cursorOf()).toMatchObject({ animate: true })
})

it('ignores a late acknowledgement for an older sequence', async () => {
  vi.useFakeTimers()
  const { cursors, win, cursorOf } = fixture()
  cursors.activate(win, 'tab')
  const first = cursors.move(win, 'tab', { x: 1, y: 1 })
  const firstSequence = cursorOf()!.moveSequence!
  await vi.advanceTimersByTimeAsync(CURSOR_ARRIVAL_TIMEOUT_MS)
  await first

  const second = cursors.move(win, 'tab', { x: 2, y: 2 })
  cursors.arrived(win, 'tab', firstSequence)
  expect(await settled(second)).toBe(false)
  cursors.arrived(win, 'tab', cursorOf()!.moveSequence!)
  await second
})

it('ignores acknowledgements from another window or for another tab', async () => {
  const { cursors, win, otherWin, add, cursorOf } = fixture()
  add(win, 'other')
  cursors.activate(win, 'tab')
  const move = cursors.move(win, 'tab', { x: 1, y: 1 })
  const sequence = cursorOf()!.moveSequence!

  cursors.arrived(otherWin, 'tab', sequence)
  cursors.arrived(win, 'other', sequence)
  expect(await settled(move)).toBe(false)

  cursors.arrived(win, 'tab', sequence)
  await move
})

it('resolves a move at the arrival bound without failing', async () => {
  vi.useFakeTimers()
  const { cursors, win } = fixture()
  cursors.activate(win, 'tab')
  const move = cursors.move(win, 'tab', { x: 10, y: 20 })
  const done = vi.fn()
  void move.then(done)
  await vi.advanceTimersByTimeAsync(CURSOR_ARRIVAL_TIMEOUT_MS - 1)
  expect(done).not.toHaveBeenCalled()
  await vi.advanceTimersByTimeAsync(1)
  await expect(move).resolves.toBeUndefined()
  expect(vi.getTimerCount()).toBe(0)
})

it.each([
  ['deactivation', (ctx: ReturnType<typeof fixture>) => ctx.cursors.deactivate(ctx.win, 'tab')],
  ['release', (ctx: ReturnType<typeof fixture>) => ctx.cursors.release(ctx.win, 'tab')],
  ['removal of the tab', (ctx: ReturnType<typeof fixture>) => ctx.cursors.forget(ctx.win, 'tab')]
])('releases pending waiters on %s', async (_name, end) => {
  vi.useFakeTimers()
  const ctx = fixture()
  ctx.cursors.activate(ctx.win, 'tab')
  const move = ctx.cursors.move(ctx.win, 'tab', { x: 10, y: 20 })
  end(ctx)
  await move
  expect(vi.getTimerCount()).toBe(0)
})
