import type { ResolveWindowApps, ResolvedWindowApp } from './appIdentity'
import { ComputerApi, type AppRef } from './computerApi'
import { isComputerApiMethod } from './methods'
import type { CuaDriverClient, DriverToolResult } from './cuaDriverClient'
import { DriverUnavailableError } from './cuaDriverClient'

export interface ComputerUseSettingsHost {
  getAlwaysAllowedApps(): AppRef[]
}

export interface ComputerUseIndicator {
  show(onStop: () => void): void
  hide(): void
  suspendEscape<T>(run: () => Promise<T>): Promise<T>
}

export interface ComputerUseCallContext {
  threadId: string
  turnId?: string
  evaluationId: string
  signal: AbortSignal
  requestApproval(app: AppRef): Promise<boolean>
  /** Pauses the outer evaluation deadline; the returned function resumes it. */
  pauseTimeout(): () => void
  emitImage(image: { mediaType: string; dataBase64: string }): Promise<void>
}

export interface ComputerUseManagerDeps {
  createDriver(): CuaDriverClient
  resolveWindowApps: ResolveWindowApps
  settings: ComputerUseSettingsHost
  indicator: ComputerUseIndicator
  platform?: NodeJS.Platform
}

type StopReason = 'user' | 'locked'

interface TurnState {
  key: string
  threadId: string
  turnId: string
  approved: Set<string>
  identities: Map<number, ResolvedWindowApp>
  api: ComputerApi
  context: ComputerUseCallContext
}

const IDLE_WAIT_MS = 3000

function turnKey(threadId: string, turnId: string): string {
  return `${threadId}\u0000${turnId}`
}

function stoppedError(reason: StopReason): Error {
  return reason === 'locked'
    ? new Error('computer_use_stopped: the desktop was locked. Stop using dotcraft.computer in this turn and ask the user to unlock it before trying again.')
    : new Error('computer_use_stopped: the user stopped computer use. Do not call dotcraft.computer again in this turn; tell the user what was done.')
}

export class ComputerUseManager {
  private driver: CuaDriverClient | null = null
  private turn: TurnState | null = null
  private readonly stoppedTurns = new Map<string, StopReason>()
  private busy = false
  private idleWaiters: Array<() => void> = []

  constructor(private readonly deps: ComputerUseManagerDeps) {}

  hasActiveTurn(threadId: string, turnId: string): boolean {
    return this.turn?.threadId === threadId && this.turn.turnId === turnId
  }

  async handleHostCall(method: string, args: unknown, context: ComputerUseCallContext): Promise<unknown> {
    if ((this.deps.platform ?? process.platform) !== 'win32') throw new Error('computer use is only available on Windows.')
    if (!isComputerApiMethod(method)) throw new Error(`invalid_method: dotcraft.computer.${method} does not exist.`)
    const turnId = context.turnId ?? context.evaluationId
    const key = turnKey(context.threadId, turnId)
    const stopped = this.stoppedTurns.get(key)
    if (stopped) throw stoppedError(stopped)
    if (this.busy) throw new Error('computer_use_busy: another computer use request is running on this computer; wait for it to finish and try again.')

    this.busy = true
    try {
      const turn = await this.enterTurn(context, turnId, key)
      turn.context = context
      if (!this.driver?.running) {
        const driver = this.deps.createDriver()
        this.driver = driver
        await driver.start()
      }
      return await turn.api.invoke(method, args)
    } catch (error) {
      const stoppedDuringCall = this.stoppedTurns.get(key)
      throw stoppedDuringCall ? stoppedError(stoppedDuringCall) : error
    } finally {
      this.busy = false
      const waiters = this.idleWaiters
      this.idleWaiters = []
      for (const resolve of waiters) resolve()
    }
  }

  stop(reason: StopReason): void {
    const turn = this.turn
    if (!turn) return
    this.stoppedTurns.set(turn.key, reason)
    this.turn = null
    this.deps.indicator.hide()
    this.driver?.kill()
    this.driver = null
  }

  handleTurnEnded(threadId: string, turnId: string): void {
    const key = turnKey(threadId, turnId)
    this.stoppedTurns.delete(key)
    if (this.turn?.key === key) void this.closeTurn()
  }

  async dispose(): Promise<void> {
    this.turn = null
    this.stoppedTurns.clear()
    this.deps.indicator.hide()
    this.driver?.kill()
    this.driver = null
  }

  private async enterTurn(context: ComputerUseCallContext, turnId: string, key: string): Promise<TurnState> {
    if (this.turn?.key === key) return this.turn
    if (this.turn) await this.closeDriver()
    const turn: TurnState = {
      key,
      threadId: context.threadId,
      turnId,
      approved: new Set(),
      identities: new Map(),
      context,
      api: undefined!
    }
    turn.api = new ComputerApi({
      call: (tool, toolArgs, timeoutMs) => this.callDriver(tool, toolArgs, timeoutMs),
      resolveWindows: (hwnds) => this.resolveWindows(turn, hwnds),
      authorize: (app) => this.authorize(turn, app),
      emitImage: (image) => turn.context.emitImage(image),
      injectingEscape: (run) => this.deps.indicator.suspendEscape(run)
    })
    this.turn = turn
    this.deps.indicator.show(() => this.stop('user'))
    return turn
  }

  private async closeTurn(): Promise<void> {
    this.turn = null
    this.deps.indicator.hide()
    if (this.busy) {
      await Promise.race([
        new Promise<void>((resolve) => this.idleWaiters.push(resolve)),
        new Promise<void>((resolve) => setTimeout(resolve, IDLE_WAIT_MS))
      ])
    }
    if (!this.turn) await this.closeDriver()
  }

  private async closeDriver(): Promise<void> {
    const driver = this.driver
    this.driver = null
    await driver?.close()
  }

  private callDriver(tool: string, toolArgs: Record<string, unknown>, timeoutMs = 10_000): Promise<DriverToolResult> {
    const driver = this.driver
    if (!driver?.running) return Promise.reject(new DriverUnavailableError('the driver is not running'))
    return driver.callTool(tool, toolArgs, timeoutMs)
  }

  private async resolveWindows(turn: TurnState, hwnds: number[]): Promise<Map<number, ResolvedWindowApp>> {
    const missing = hwnds.filter((hwnd) => !turn.identities.has(hwnd))
    if (missing.length > 0) {
      for (const identity of await this.deps.resolveWindowApps(missing)) turn.identities.set(identity.hwnd, identity)
    }
    const resolved = new Map<number, ResolvedWindowApp>()
    for (const hwnd of hwnds) {
      const identity = turn.identities.get(hwnd)
      if (identity) resolved.set(hwnd, identity)
    }
    return resolved
  }

  private async authorize(turn: TurnState, app: AppRef): Promise<void> {
    const id = app.id.toLowerCase()
    if (turn.approved.has(id)) return
    if (this.deps.settings.getAlwaysAllowedApps().some((allowed) => allowed.id.toLowerCase() === id)) {
      turn.approved.add(id)
      return
    }
    const resume = turn.context.pauseTimeout()
    let approved: boolean
    try {
      approved = await turn.context.requestApproval(app)
    } finally {
      resume()
    }
    const stopped = this.stoppedTurns.get(turn.key)
    if (stopped) throw stoppedError(stopped)
    if (!approved) throw new Error(`app_not_approved: the user did not allow DotCraft to use ${app.displayName}.`)
    turn.approved.add(id)
  }
}
