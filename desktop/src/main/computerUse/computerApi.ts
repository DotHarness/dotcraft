import type { ResolvedWindowApp } from './appIdentity'
import { isBlockedApp } from './blockedApps'
import type { DriverToolResult } from './cuaDriverClient'
import { parseKeyChord } from './keyNames'
import type { ComputerApiMethod } from './methods'

export interface AppRef {
  id: string
  displayName: string
}

interface ComputerWindow {
  id: number
  pid: number
  title: string
  app: AppRef
}

export interface ComputerApiHost {
  call(tool: string, args: Record<string, unknown>, timeoutMs?: number): Promise<DriverToolResult>
  resolveWindows(hwnds: number[]): Promise<Map<number, ResolvedWindowApp>>
  authorize(app: AppRef): Promise<void>
  emitImage(image: { mediaType: string; dataBase64: string }): Promise<void>
  injectingEscape<T>(run: () => Promise<T>): Promise<T>
}

const DELIVERY_MODE = 'foreground'
const DEFAULT_TIMEOUT_MS = 10_000
const SLOW_TIMEOUT_MS = 15_000
const MAX_IMAGE_DIMENSION = 1568

type Args = Record<string, unknown>

function driverError(result: DriverToolResult): Error {
  const structured = result.structuredContent ?? {}
  const text = result.content.filter((block) => block.type === 'text' && block.text).map((block) => block.text).join('\n').trim()
  const code = typeof structured.code === 'string'
    ? structured.code
    : typeof structured.refusal === 'string' ? structured.refusal : undefined
  const message = text || (typeof structured.message === 'string' ? structured.message : 'the driver rejected the request')
  return new Error(code && !message.startsWith(`${code}`) ? `${code}: ${message}` : message)
}

function args(value: unknown): Args {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Args : {}
}

function requiredNumber(source: Args, name: string): number {
  const value = source[name]
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`invalid_arguments: ${name} must be a number.`)
  return value
}

function optionalNumber(source: Args, name: string): number | undefined {
  return source[name] === undefined ? undefined : requiredNumber(source, name)
}

function requiredString(source: Args, name: string): string {
  const value = source[name]
  if (typeof value !== 'string') throw new Error(`invalid_arguments: ${name} must be a string.`)
  return value
}

function windowHandle(source: Args): number {
  const window = source.window
  if (typeof window === 'number') return window
  if (window && typeof window === 'object' && typeof (window as Args).id === 'number') return (window as Args).id as number
  throw new Error('invalid_arguments: window must be a window from list_windows().')
}

function aumidFromLaunchPath(launchPath: unknown): string | undefined {
  if (typeof launchPath !== 'string') return undefined
  const match = /^shell:appsfolder\\(.+![^\s]+)$/i.exec(launchPath.trim())
  return match?.[1]
}

export class ComputerApi {
  private readonly snapshots = new Map<number, string>()
  private readonly catalog = new Map<string, AppRef>()

  constructor(private readonly host: ComputerApiHost) {}

  async invoke(method: ComputerApiMethod, rawArgs: unknown): Promise<unknown> {
    const input = args(rawArgs)
    switch (method) {
      case 'list_apps': return this.listApps()
      case 'list_windows': return this.listWindows()
      case 'get_window': return this.getWindow(requiredNumber(input, 'id'))
      case 'launch_app': return this.launchApp(input)
      case 'get_window_state': return this.getWindowState(input)
      case 'click': return this.click(input)
      case 'type_text': return this.act(input, 'type_text', { text: requiredString(input, 'text') })
      case 'press_key': return this.pressKey(input)
      case 'scroll': return this.scroll(input)
      case 'set_value': return this.setValue(input)
      case 'drag': return this.drag(input)
      case 'activate_window': return this.activateWindow(input)
    }
  }

  private async call(tool: string, toolArgs: Args, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<DriverToolResult> {
    const result = await this.host.call(tool, toolArgs, timeoutMs)
    if (result.isError) throw driverError(result)
    return result
  }

  private async listApps(): Promise<Array<AppRef & { isRunning: boolean }>> {
    const result = await this.call('list_apps', {}, SLOW_TIMEOUT_MS)
    const apps = Array.isArray(result.structuredContent?.apps) ? result.structuredContent.apps as Args[] : []
    const listed: Array<AppRef & { isRunning: boolean }> = []
    for (const app of apps) {
      const id = app.kind === 'uwp' ? aumidFromLaunchPath(app.launch_path) : typeof app.bundle_id === 'string' ? app.bundle_id : undefined
      if (!id || isBlockedApp(id)) continue
      const displayName = typeof app.name === 'string' && app.name ? app.name : id
      this.catalog.set(id.toLowerCase(), { id, displayName })
      listed.push({ id, displayName, isRunning: app.running === true })
    }
    return listed
  }

  private async listWindows(): Promise<ComputerWindow[]> {
    const result = await this.call('list_windows', { on_screen_only: true })
    const windows = Array.isArray(result.structuredContent?.windows) ? result.structuredContent.windows as Args[] : []
    const handles = windows.map((window) => window.window_id).filter((id): id is number => typeof id === 'number')
    const identities = await this.host.resolveWindows(handles)
    const listed: ComputerWindow[] = []
    for (const window of windows) {
      const identity = typeof window.window_id === 'number' ? identities.get(window.window_id) : undefined
      if (!identity || isBlockedApp(identity.id, identity.exePath)) continue
      listed.push({
        id: identity.hwnd,
        pid: identity.windowPid,
        title: typeof window.title === 'string' ? window.title : '',
        app: { id: identity.id, displayName: identity.displayName }
      })
    }
    return listed
  }

  private async getWindow(hwnd: number): Promise<ComputerWindow> {
    const window = (await this.listWindows()).find((candidate) => candidate.id === hwnd)
    if (!window) throw new Error(`window_target_not_found: window ${hwnd} is not an on-screen window of an allowed app.`)
    return window
  }

  private async launchApp(input: Args): Promise<void> {
    const raw = input.app
    const id = typeof raw === 'string' ? raw : raw && typeof raw === 'object' && typeof (raw as Args).id === 'string' ? (raw as Args).id as string : undefined
    if (!id) throw new Error('invalid_arguments: app must be an id from list_apps().')
    if (isBlockedApp(id)) throw new Error('app_blocked: this application cannot be operated.')
    const app = this.catalog.get(id.toLowerCase())
    if (!app) throw new Error('invalid_arguments: app must be an id returned by list_apps() in this turn.')
    await this.host.authorize(app)
    await this.call('launch_app', app.id.includes('!') ? { aumid: app.id } : { path: app.id }, SLOW_TIMEOUT_MS)
  }

  /** The model-supplied `window.app` is never trusted; identity always comes from the handle. */
  private async target(input: Args): Promise<{ hwnd: number; identity: ResolvedWindowApp }> {
    const hwnd = windowHandle(input)
    const identity = (await this.host.resolveWindows([hwnd])).get(hwnd)
    if (!identity) throw new Error(`app_unidentified: the application of window ${hwnd} could not be identified.`)
    if (isBlockedApp(identity.id, identity.exePath)) throw new Error('app_blocked: this application cannot be operated.')
    await this.host.authorize({ id: identity.id, displayName: identity.displayName })
    return { hwnd, identity }
  }

  private async getWindowState(input: Args): Promise<unknown> {
    const includeScreenshot = input.include_screenshot !== false
    const includeText = input.include_text === true
    if (!includeScreenshot && !includeText) {
      throw new Error('invalid_arguments: include_screenshot and include_text cannot both be false.')
    }
    const { hwnd, identity } = await this.target(input)
    const result = await this.call('get_window_state', {
      pid: identity.windowPid,
      window_id: hwnd,
      include_screenshot: includeScreenshot,
      include_accessibility_tree: includeText,
      max_dimension: MAX_IMAGE_DIMENSION
    })
    const state = result.structuredContent ?? {}
    const snapshotId = typeof state.snapshot_id === 'string' ? state.snapshot_id : undefined
    if (snapshotId) this.snapshots.set(hwnd, snapshotId)
    for (const block of result.content) {
      if (block.type === 'image' && block.data) {
        await this.host.emitImage({ mediaType: block.mimeType ?? 'image/png', dataBase64: block.data })
      }
    }
    const window: ComputerWindow = {
      id: hwnd,
      pid: identity.windowPid,
      title: typeof state.window_title === 'string' ? state.window_title : '',
      app: { id: identity.id, displayName: identity.displayName }
    }
    const screenshots = includeScreenshot && typeof state.screenshot_width === 'number'
      ? [{ width: state.screenshot_width, height: state.screenshot_height }]
      : []
    return {
      window,
      screenshots,
      accessibility: includeText ? { tree: typeof state.tree_markdown === 'string' ? state.tree_markdown : '' } : null
    }
  }

  private async act(input: Args, tool: string, extra: Args, timeoutMs = DEFAULT_TIMEOUT_MS): Promise<void> {
    const { hwnd, identity } = await this.target(input)
    await this.call(tool, { pid: identity.windowPid, window_id: hwnd, delivery_mode: DELIVERY_MODE, ...extra }, timeoutMs)
  }

  private elementTarget(hwnd: number, input: Args): Args {
    const index = requiredNumber(input, 'element_index')
    const snapshotId = this.snapshots.get(hwnd)
    if (!snapshotId) throw new Error('snapshot_id_required: observe the window with get_window_state({ include_text: true }) first.')
    return { element_index: index, snapshot_id: snapshotId }
  }

  private async click(input: Args): Promise<void> {
    const button = input.mouse_button === undefined ? 'left' : requiredString(input, 'mouse_button')
    if (!['left', 'right', 'middle'].includes(button)) throw new Error('invalid_arguments: mouse_button must be left, right, or middle.')
    const count = optionalNumber(input, 'click_count') ?? 1
    const hwnd = windowHandle(input)
    let location: Args
    if (input.element_index !== undefined) {
      location = this.elementTarget(hwnd, input)
    } else {
      location = { x: requiredNumber(input, 'x'), y: requiredNumber(input, 'y') }
    }
    await this.act(input, 'click', { ...location, button, count })
  }

  private async pressKey(input: Args): Promise<void> {
    const press = parseKeyChord(requiredString(input, 'key'))
    const send = () => this.act(input, 'press_key', { key: press.key, ...(press.modifiers.length ? { modifiers: press.modifiers } : {}) })
    await (press.key === 'escape' ? this.host.injectingEscape(send) : send())
  }

  private async scroll(input: Args): Promise<void> {
    const scrollX = optionalNumber(input, 'scrollX') ?? 0
    const scrollY = optionalNumber(input, 'scrollY') ?? 0
    if (scrollX === 0 && scrollY === 0) throw new Error('invalid_arguments: scrollX or scrollY must be non-zero.')
    const vertical = Math.abs(scrollY) >= Math.abs(scrollX)
    const delta = vertical ? scrollY : scrollX
    const direction = vertical ? (delta > 0 ? 'down' : 'up') : (delta > 0 ? 'right' : 'left')
    await this.act(input, 'scroll', {
      direction,
      amount: Math.min(50, Math.max(1, Math.round(Math.abs(delta)))),
      x: requiredNumber(input, 'x'),
      y: requiredNumber(input, 'y')
    })
  }

  private async setValue(input: Args): Promise<void> {
    const value = requiredString(input, 'value')
    const { hwnd, identity } = await this.target(input)
    await this.call('set_value', { pid: identity.windowPid, window_id: hwnd, ...this.elementTarget(hwnd, input), value })
  }

  private async drag(input: Args): Promise<void> {
    await this.act(input, 'drag', {
      from_x: requiredNumber(input, 'from_x'),
      from_y: requiredNumber(input, 'from_y'),
      to_x: requiredNumber(input, 'to_x'),
      to_y: requiredNumber(input, 'to_y')
    })
  }

  private async activateWindow(input: Args): Promise<void> {
    const { hwnd, identity } = await this.target(input)
    await this.call('bring_to_front', { pid: identity.windowPid, window_id: hwnd })
  }
}
