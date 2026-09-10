import { EventEmitter } from 'events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetCommand, PetEvent } from '../../shared/desktopPet'
import { PET_RETURN_DURATION } from '../../shared/desktopPetMotion'

const mocks = vi.hoisted(() => ({ handler: null as null | ((event: unknown, command: PetCommand) => void), windows: [] as any[] }))
vi.mock('electron', async () => {
  const { EventEmitter } = await import('events')
  class Window extends EventEmitter {
    destroyed = false
    options: any
    private contents = Object.assign(new EventEmitter(), {
      send: vi.fn(), isDestroyed: () => false, getZoomFactor: () => 1.25,
      getURL: () => 'http://localhost:5173', setWindowOpenHandler: vi.fn(), setBackgroundThrottling: vi.fn()
    })
    get webContents() {
      if (this.destroyed) throw new TypeError('Object has been destroyed')
      return this.contents
    }
    setOpacity = vi.fn()
    show = vi.fn(() => this.emit('show'))
    showInactive = vi.fn()
    hide = vi.fn()
    focus = vi.fn()
    restore = vi.fn()
    setBounds = vi.fn()
    setIgnoreMouseEvents = vi.fn()
    loadURL = vi.fn(async () => {})
    loadFile = vi.fn(async () => {})
    isMinimized = () => false
    isDestroyed = () => this.destroyed
    getContentBounds = () => ({ x: -1200, y: 50, width: 1000, height: 800 })
    destroy = (): void => { this.destroyed = true; this.emit('closed') }
    constructor(options?: any) { super(); this.options = options; mocks.windows.push(this) }
  }
  return { BrowserWindow: Window,
    ipcMain: { removeHandler: () => { mocks.handler = null }, handle: (_name: string, handler: typeof mocks.handler) => { mocks.handler = handler } },
    screen: Object.assign(new EventEmitter(), {
      getDisplayNearestPoint: () => ({ workArea: { x: -1920, y: 0, width: 1920, height: 1080 } }),
      getCursorScreenPoint: () => ({ x: -500, y: 300 })
    }) }
})
import { BrowserWindow } from 'electron'
import { attachDesktopPet, restoreDesktopPet } from '../desktopPet'

const snapshot = { name: 'DotCraft', text: 'Draft', theme: 'dark' as const, locale: 'en' as const, reducedMotion: false, canChat: true, followUpMode: 'queue' as const }
let owner: any
function send(sender: any, command: PetCommand): void { mocks.handler!({ sender: sender.webContents }, command) }
function detach(pointerHeld = false): any {
  send(owner, { type: 'detach', seat: { x: 100, y: 100, width: 44, height: 44 }, point: { x: 300, y: 200 }, snapshot, pointerHeld })
  return mocks.windows[1]
}
function events(win: any): PetEvent[] { return win.webContents.send.mock.calls.map((call: any[]) => call[1]) }
let warn: ReturnType<typeof vi.spyOn>
beforeEach(() => {
  vi.useFakeTimers()
  warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
  mocks.windows.length = 0
  owner = new BrowserWindow()
  attachDesktopPet(owner)
})
afterEach(() => { owner.emit('closed'); vi.useRealTimers(); vi.restoreAllMocks() })

describe('desktop pet native ownership', () => {
  it('uses a sandboxed window with the dedicated preload', () => {
    const pet = detach()
    expect(pet.options.webPreferences).toMatchObject({ contextIsolation: true, nodeIntegration: false, sandbox: true })
    expect(pet.options.webPreferences.preload).toMatch(/[\\/]preload[\\/]pet\.js$/)
  })
  it('fades on edge entry while preserving source capture until release', async () => {
    const pet = detach(true)
    send(pet, { type: 'ready' })
    send(owner, { type: 'hidden' })
    await vi.advanceTimersByTimeAsync(350)
    expect(owner.setOpacity).toHaveBeenLastCalledWith(0)
    expect(owner.hide).not.toHaveBeenCalled()
    send(pet, { type: 'interactive', value: true })
    expect(pet.setIgnoreMouseEvents).toHaveBeenLastCalledWith(true, { forward: true })
    send(owner, { type: 'source-drag', stage: 'end' })
    expect(owner.hide).toHaveBeenCalledTimes(1)
    expect(owner.setOpacity).toHaveBeenLastCalledWith(1)
    expect(vi.getTimerCount()).toBe(0)
  })
  it('cleans up after native destruction without reading the dead window handle', () => {
    const pet = detach(true)
    expect(() => owner.destroy()).not.toThrow()
    expect(pet.destroyed).toBe(true)
    expect(mocks.handler).toBeNull()
    expect(vi.getTimerCount()).toBe(0)
    expect(() => owner.emit('closed')).not.toThrow()
  })
  it('waits for both renderers before hiding the desktop and restores through one coordinator', async () => {
    const pet = detach()
    expect(owner.hide).not.toHaveBeenCalled()
    send(pet, { type: 'ready' })
    expect(pet.showInactive).not.toHaveBeenCalled()
    expect(events(owner)).toContainEqual({ type: 'ownership', detached: true })
    send(owner, { type: 'hidden' })
    await vi.advanceTimersByTimeAsync(350)
    expect(pet.showInactive).toHaveBeenCalledTimes(1)
    expect(owner.hide).toHaveBeenCalledTimes(1)
    expect(restoreDesktopPet(owner)).toBe(true)
    send(owner, { type: 'seat', seat: { x: 120, y: 80, width: 44, height: 44 } })
    await vi.advanceTimersByTimeAsync(PET_RETURN_DURATION + 20)
    expect(owner.show).toHaveBeenCalled()
    expect(pet.destroyed).toBe(false)
    await vi.advanceTimersByTimeAsync(300)
    expect(pet.destroyed).toBe(true)
    expect(events(owner).at(-1)).toEqual({ type: 'ownership', detached: false })
    expect(restoreDesktopPet(owner)).toBe(false)
    expect(owner.setOpacity).toHaveBeenLastCalledWith(1)
  })
  it('rejects unknown senders and ignores invalid geometry', () => {
    expect(() => mocks.handler!({ sender: {} }, { type: 'return' })).toThrow('Unknown')
    send(owner, { type: 'detach', seat: { x: NaN, y: 0, width: 44, height: 44 }, point: { x: 0, y: 0 }, snapshot })
    expect(mocks.windows).toHaveLength(1)
  })
  it('restores a reachable desktop when the companion cannot become ready', async () => {
    const pet = detach()
    await vi.advanceTimersByTimeAsync(5100)
    expect(pet.destroyed).toBe(true)
    expect(owner.show).toHaveBeenCalled()
    expect(owner.setOpacity).toHaveBeenLastCalledWith(1)
    expect(events(owner).at(-1)).toEqual({ type: 'ownership', detached: false })
  })
  it('recovers immediately when the sandboxed preload fails', () => {
    const error = new Error('Cannot find module ./chunks/desktopPet.js')
    const log = vi.spyOn(console, 'error').mockImplementation(() => {})
    const pet = detach(true)

    pet.webContents.emit('preload-error', {}, 'C:/DotCraft/resources/app.asar/out/preload/pet.js', error)

    expect(log).toHaveBeenCalledWith(expect.stringContaining('companion preload failed'), error)
    expect(pet.destroyed).toBe(true)
    expect(owner.show).toHaveBeenCalled()
    expect(owner.setOpacity).toHaveBeenLastCalledWith(1)
    expect(events(owner).at(-1)).toEqual({ type: 'ownership', detached: false })
    expect(vi.getTimerCount()).toBe(0)
  })
  it('does not open duplicate companions or replay a leave animation', async () => {
    const pet = detach()
    detach()
    send(pet, { type: 'ready' }); send(owner, { type: 'hidden' })
    send(pet, { type: 'ready' }); send(owner, { type: 'hidden' })
    await vi.advanceTimersByTimeAsync(350)
    expect(mocks.windows).toHaveLength(2)
    expect(pet.showInactive).toHaveBeenCalledTimes(1)
  })
  it('forwards edits only to the owning composer and blocks decision input', () => {
    const pet = detach()
    const edit = { type: 'edit' as const, text: 'Hello', submit: true, revision: 1 }
    send(pet, edit)
    expect(events(owner)).toContainEqual(edit)
    owner.webContents.send.mockClear()
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, canChat: false } })
    send(pet, edit)
    expect(events(owner)).toEqual([])
  })
  it('accepts an optional busy flag on snapshots, rejects other shapes, and warns once', () => {
    const pet = detach()
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, busy: 'yes' } as any })
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, followUpMode: 'now' } as any })
    expect(events(pet)).toEqual([])
    expect(warn).toHaveBeenCalledTimes(1)
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, busy: true } })
    expect(events(pet)).toContainEqual({ type: 'snapshot', snapshot: { ...snapshot, busy: true } })
  })
  it('validates the status contract before relaying it to the companion', () => {
    const pet = detach()
    const status = { status: 'running', title: 'Fix', line: 'Running tests', lineTone: 'neutral', turnId: 'turn-1', canStop: true }
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status: { ...status, status: 'flying' } } as any })
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status: { ...status, line: 'x'.repeat(201) } } as any })
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status: { ...status, stopping: 'yes' } } as any })
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status: { ...status, decision: { id: 'd', question: '', operation: '', target: '', reason: '', options: [], declineValue: 'decline' } } } as any })
    expect(events(pet)).toEqual([])
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status } as any })
    expect(events(pet)).toContainEqual({ type: 'snapshot', snapshot: { ...snapshot, status } })
  })
  it('relays stop and read only for the turn the companion was shown, and keeps room for its pill', () => {
    send(owner, { type: 'detach', seat: { x: 100, y: 100, width: 44, height: 44 }, point: { x: 300, y: 800 }, snapshot })
    const pet = mocks.windows[1]
    const status = { status: 'running', title: 'Fix', line: 'Running tests', lineTone: 'neutral', turnId: 'turn-1', canStop: true }
    send(pet, { type: 'stop', turnId: 'turn-1' })
    expect(events(owner).filter((event) => event.type === 'stop')).toEqual([])
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status } as any })
    send(pet, { type: 'stop', turnId: 'turn-9' })
    send(owner, { type: 'stop', turnId: 'turn-1' } as any)
    expect(events(owner).filter((event) => event.type === 'stop')).toEqual([])
    send(pet, { type: 'stop', turnId: 'turn-1' })
    expect(events(owner)).toContainEqual({ type: 'stop', turnId: 'turn-1' })
    send(pet, { type: 'read', turnId: 'turn-1' })
    expect(events(owner)).toContainEqual({ type: 'read', turnId: 'turn-1' })
    send(pet, { type: 'read', turnId: '' })
    expect(events(owner).filter((event) => event.type === 'read')).toHaveLength(1)

    send(pet, { type: 'ready' }); send(owner, { type: 'hidden' })
    send(pet, { type: 'layout', height: 200 })
    let position = events(pet).filter((event) => event.type === 'position').at(-1) as any
    expect(position.point.y).toBe(1080 - 59 - 212)
    send(pet, { type: 'layout', height: 1000 })
    position = events(pet).filter((event) => event.type === 'position').at(-1) as any
    expect(position.point.y).toBe(1080 - 59 - 540)
    send(pet, { type: 'layout', height: 0 })
    position = events(pet).filter((event) => event.type === 'position').at(-1) as any
    expect(position.point.y).toBe(1080 - 59 - 540)
  })
  it('relays a decision only when it names the approval on display and one of its options', () => {
    const pet = detach()
    const decision = { id: 'tool:r1', question: 'Run?', operation: 'run', target: 'npm test', reason: '', declineValue: 'decline', options: [{ value: 'accept', label: 'Allow' }, { value: 'decline', label: 'Deny' }] }
    const status = { status: 'waiting', title: 'Fix', line: 'run npm test', lineTone: 'warning', turnId: 'turn-1', canStop: false, decision }
    send(pet, { type: 'decision', id: 'tool:r1', value: 'accept' })
    send(owner, { type: 'snapshot', snapshot: { ...snapshot, status } as any })
    send(pet, { type: 'decision', id: 'tool:r2', value: 'accept' })
    send(pet, { type: 'decision', id: 'tool:r1', value: 'acceptAlways' })
    send(owner, { type: 'decision', id: 'tool:r1', value: 'accept' } as any)
    expect(events(owner).filter((event) => event.type === 'decision')).toEqual([])
    send(pet, { type: 'decision', id: 'tool:r1', value: 'accept' })
    expect(events(owner)).toContainEqual({ type: 'decision', id: 'tool:r1', value: 'accept' })
  })
  it('recovers on renderer failure and cleans up its listeners', () => {
    const pet = detach()
    pet.webContents.emit('render-process-gone')
    expect(pet.destroyed).toBe(true)
    expect(owner.show).toHaveBeenCalled()
    owner.emit('closed')
    expect(mocks.handler).toBeNull()
    expect((owner as EventEmitter).listenerCount('show')).toBe(0)
  })
  it('cancels an unfinished handoff without waiting on a renderer that is not ready', () => {
    const pet = detach()
    expect(restoreDesktopPet(owner)).toBe(true)
    expect(pet.destroyed).toBe(true)
    expect(owner.setOpacity).toHaveBeenLastCalledWith(1)
    expect(events(owner).at(-1)).toEqual({ type: 'ownership', detached: false })
  })
})
