import { describe, expect, it, vi } from 'vitest'

import type { ResolvedWindowApp } from '../appIdentity'
import type { AppRef } from '../computerApi'
import { ComputerUseManager, type ComputerUseCallContext } from '../ComputerUseManager'
import type { CuaDriverClient, DriverToolResult } from '../cuaDriverClient'

const NOTEPAD: ResolvedWindowApp = {
  hwnd: 101,
  windowPid: 11,
  id: 'C:\\Windows\\System32\\notepad.exe',
  displayName: 'Notepad',
  exePath: 'C:\\Windows\\System32\\notepad.exe'
}

const TERMINAL: ResolvedWindowApp = {
  hwnd: 202,
  windowPid: 22,
  id: 'Microsoft.WindowsTerminal_8wekyb3d8bbwe!App',
  displayName: 'Terminal'
}

class FakeDriver {
  running = false
  calls: Array<{ tool: string; args: Record<string, unknown> }> = []
  closed = 0
  killed = 0
  block: Promise<void> | null = null

  async start(): Promise<void> {
    this.running = true
  }

  async callTool(tool: string, args: Record<string, unknown>): Promise<DriverToolResult> {
    this.calls.push({ tool, args })
    if (this.block) await this.block
    if (!this.running) throw new Error('driver_unavailable: the driver was stopped')
    if (tool === 'get_window_state') {
      return {
        content: [{ type: 'text', text: 'ok' }, { type: 'image', data: 'cG5n', mimeType: 'image/png' }],
        structuredContent: {
          snapshot_id: 'snap-1', capture_id: 'cap-1', screenshot_width: 800, screenshot_height: 600,
          window_title: 'Untitled - Notepad', tree_markdown: '[0] Document'
        }
      }
    }
    if (tool === 'list_windows') {
      return {
        content: [],
        structuredContent: { windows: [{ window_id: 101, pid: 11, title: 'Untitled - Notepad' }, { window_id: 202, pid: 22, title: 'Terminal' }] }
      }
    }
    return { content: [{ type: 'text', text: 'ok' }], structuredContent: { effect: 'confirmed' } }
  }

  async close(): Promise<void> {
    this.closed += 1
    this.running = false
  }

  kill(): void {
    this.killed += 1
    this.running = false
  }
}

function setup(options: { alwaysAllowed?: AppRef[] } = {}) {
  const drivers: FakeDriver[] = []
  const alwaysAllowed = [...(options.alwaysAllowed ?? [])]
  const indicator = {
    show: vi.fn(),
    hide: vi.fn(),
    suspendEscape: vi.fn(<T>(run: () => Promise<T>) => run())
  }
  const manager = new ComputerUseManager({
    platform: 'win32',
    createDriver: () => {
      const driver = new FakeDriver()
      drivers.push(driver)
      return driver as unknown as CuaDriverClient
    },
    resolveWindowApps: async (hwnds) => [NOTEPAD, TERMINAL].filter((app) => hwnds.includes(app.hwnd)),
    settings: { getAlwaysAllowedApps: () => alwaysAllowed },
    indicator
  })
  const context = (overrides: Partial<ComputerUseCallContext> = {}): ComputerUseCallContext => ({
    threadId: 'thread-1',
    turnId: 'turn-1',
    evaluationId: 'eval-1',
    signal: new AbortController().signal,
    requestApproval: vi.fn(async () => true),
    pauseTimeout: vi.fn(() => vi.fn()),
    emitImage: vi.fn(async () => {}),
    ...overrides
  })
  return { manager, drivers, indicator, context }
}

describe('ComputerUseManager', () => {
  it('asks once per turn for an unknown app, pausing the evaluation deadline while waiting', async () => {
    const { manager, context } = setup()
    const resume = vi.fn()
    const ctx = context({ pauseTimeout: vi.fn(() => resume) })

    await manager.handleHostCall('type_text', { window: { id: 101, app: { id: 'spoofed' } }, text: 'hi' }, ctx)
    await manager.handleHostCall('press_key', { window: { id: 101 }, key: 'ctrl+s' }, ctx)

    expect(ctx.requestApproval).toHaveBeenCalledTimes(1)
    expect(ctx.requestApproval).toHaveBeenCalledWith({ id: NOTEPAD.id, displayName: 'Notepad' })
    expect(ctx.pauseTimeout).toHaveBeenCalledTimes(1)
    expect(resume).toHaveBeenCalledTimes(1)
  })

  it('fails with app_not_approved when the user declines', async () => {
    const { manager, drivers, context } = setup()
    const ctx = context({ requestApproval: vi.fn(async () => false) })

    await expect(manager.handleHostCall('type_text', { window: { id: 101 }, text: 'hi' }, ctx))
      .rejects.toThrow(/^app_not_approved/)
    expect(drivers[0].calls.some((call) => call.tool === 'type_text')).toBe(false)
  })

  it('does not ask for always-allowed apps and refuses blocked apps without asking', async () => {
    const { manager, context } = setup({ alwaysAllowed: [{ id: NOTEPAD.id.toUpperCase(), displayName: 'Notepad' }] })
    const ctx = context()

    await manager.handleHostCall('click', { window: { id: 101 }, x: 10, y: 20, screenshotId: 'cap-1' }, ctx)
    await expect(manager.handleHostCall('type_text', { window: { id: 202 }, text: 'rm -rf' }, ctx))
      .rejects.toThrow(/^app_blocked/)

    expect(ctx.requestApproval).not.toHaveBeenCalled()
  })

  it('lists only windows of allowed apps with runtime-resolved identity', async () => {
    const { manager, context } = setup()

    const windows = await manager.handleHostCall('list_windows', {}, context())

    expect(windows).toEqual([{ id: 101, pid: 11, title: 'Untitled - Notepad', app: { id: NOTEPAD.id, displayName: 'Notepad' } }])
  })

  it('returns observations, emits screenshots and binds element indexes to the latest snapshot', async () => {
    const { manager, drivers, context } = setup()
    const ctx = context()

    const state = await manager.handleHostCall('get_window_state', { window: { id: 101 }, include_text: true }, ctx)
    await manager.handleHostCall('click', { window: { id: 101 }, element_index: 0 }, ctx)

    expect(state).toMatchObject({
      screenshots: [{ id: 'cap-1', width: 800, height: 600 }],
      accessibility: { tree: '[0] Document' }
    })
    expect(ctx.emitImage).toHaveBeenCalledWith({ mediaType: 'image/png', dataBase64: 'cG5n' })
    expect(drivers[0].calls.at(-1)).toMatchObject({
      tool: 'click',
      args: { pid: 11, window_id: 101, element_index: 0, snapshot_id: 'snap-1', delivery_mode: 'foreground' }
    })
  })

  it('rejects a concurrent request instead of queueing it', async () => {
    const { manager, drivers, context } = setup()
    let release!: () => void
    const first = manager.handleHostCall('list_windows', {}, context())
    await vi.waitFor(() => expect(drivers).toHaveLength(1))
    drivers[0].block = new Promise((resolve) => { release = resolve })
    await first
    const blocked = manager.handleHostCall('list_windows', {}, context())
    await vi.waitFor(() => expect(drivers[0].calls.length).toBeGreaterThan(1))

    await expect(manager.handleHostCall('list_windows', {}, context({ threadId: 'thread-2' })))
      .rejects.toThrow(/^computer_use_busy/)
    release()
    await blocked
  })

  it('stops the turn on request and fails later calls in that turn only', async () => {
    const { manager, drivers, indicator, context } = setup()
    await manager.handleHostCall('list_windows', {}, context())
    expect(indicator.show).toHaveBeenCalledTimes(1)
    const onStop = indicator.show.mock.calls[0][0] as () => void

    onStop()

    expect(drivers[0].killed).toBe(1)
    expect(indicator.hide).toHaveBeenCalled()
    await expect(manager.handleHostCall('list_windows', {}, context())).rejects.toThrow(/^computer_use_stopped/)
    await expect(manager.handleHostCall('list_windows', {}, context({ turnId: 'turn-2' }))).resolves.toBeDefined()
  })

  it('closes the driver when the owning turn ends', async () => {
    const { manager, drivers, context } = setup()
    await manager.handleHostCall('list_windows', {}, context())
    expect(manager.hasActiveTurn('thread-1', 'turn-1')).toBe(true)

    manager.handleTurnEnded('thread-1', 'turn-1')

    await vi.waitFor(() => expect(drivers[0].closed).toBe(1))
    expect(manager.hasActiveTurn('thread-1', 'turn-1')).toBe(false)
  })

  it('suspends the Escape shortcut while injecting Escape and rejects Windows-logo chords', async () => {
    const { manager, indicator, context } = setup({ alwaysAllowed: [{ id: NOTEPAD.id, displayName: 'Notepad' }] })
    const ctx = context()

    await manager.handleHostCall('press_key', { window: { id: 101 }, key: 'Escape' }, ctx)
    await expect(manager.handleHostCall('press_key', { window: { id: 101 }, key: 'super+r' }, ctx))
      .rejects.toThrow(/^invalid_key/)

    expect(indicator.suspendEscape).toHaveBeenCalledTimes(1)
  })
})
