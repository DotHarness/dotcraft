import { win32 } from 'path'
import { homedir } from 'os'
import { hubLockPath, parseHubBaseUrl, readHubLockFromPath, type HubLockInfo } from '@dotcraft/sdk/hub'
import {
  getDesktopProcessRegistryDirectory, isProcessAlive, readLiveDesktopRegistrations,
  requestDesktopIdentity, requestDesktopQuit, type DesktopProcessRegistration
} from './desktopProcessRegistry'
import { getTrayLockPath, readTrayLockInfo, requestTrayIdentity, requestTrayShutdown, type TrayControlEndpoint, type TrayLockInfo } from './trayLock'
import { acquireAppUpdateLease, type AppUpdateLease } from './appUpdateLease'

type ProcessIdentity = { pid: number; executable: string }

interface AppUpdateProcessControls {
  desktops: DesktopProcessRegistration[]
  tray: TrayLockInfo | null
  hub: HubLockInfo | null
}

export interface AppUpdateProcessDependencies {
  readControls(): AppUpdateProcessControls
  identifyDesktop(registration: DesktopProcessRegistration): Promise<ProcessIdentity | null>
  identifyTray(endpoint: TrayControlEndpoint): Promise<ProcessIdentity | null>
  quitDesktop(registration: DesktopProcessRegistration): Promise<boolean>
  quitTray(endpoint: TrayControlEndpoint): Promise<boolean>
  stopHub(hub: HubLockInfo, signal: AbortSignal): Promise<void>
  acquireLease(): Promise<AppUpdateLease>
  isAlive(pid: number): boolean
  now(): number
  delay(ms: number): Promise<void>
}

export interface PrepareAppUpdateProcessesOptions {
  desktopExecutable: string
  runtimeExecutable?: string
  currentPid?: number
  homeDir?: string
  quiesce(signal: AbortSignal): Promise<void>
  dependencies?: Partial<AppUpdateProcessDependencies>
  timeoutMs?: number
  log?: (stage: string, details: Record<string, unknown>) => void
}

export class AppUpdatePreparationError extends Error {
  constructor(readonly stage: string, readonly blockers: ProcessIdentity[], message: string) {
    super([message, `stage=${stage}`, ...blockers.map(({ pid, executable }) => `pid=${pid}, executable=${executable}`)].join('\n'))
    this.name = 'AppUpdatePreparationError'
  }
}

function samePath(left: string | undefined | null, right: string | undefined): boolean {
  return Boolean(left && right && win32.resolve(left).toLowerCase() === win32.resolve(right).toLowerCase())
}

function defaults(options: PrepareAppUpdateProcessesOptions): AppUpdateProcessDependencies {
  const home = options.homeDir ?? homedir()
  const trayPath = getTrayLockPath(home)
  return {
    readControls: () => ({
      desktops: readLiveDesktopRegistrations(getDesktopProcessRegistryDirectory(home)),
      tray: readTrayLockInfo(trayPath),
      hub: readHubLockFromPath(hubLockPath(home))
    }),
    identifyDesktop: (registration) => requestDesktopIdentity(registration.endpoint),
    identifyTray: (endpoint) => requestTrayIdentity(endpoint),
    quitDesktop: (registration) => requestDesktopQuit(registration.endpoint),
    quitTray: (endpoint) => requestTrayShutdown(endpoint),
    stopHub: async (hub, signal) => {
      const base = parseHubBaseUrl(hub.apiBaseUrl)
      const response = await fetch(new URL('/v1/status', base), { signal })
      if (!response.ok) throw new Error(`Hub status failed with HTTP ${response.status}.`)
      const status = await response.json() as { pid?: number; binaryPath?: string }
      if (status.pid !== hub.pid || !samePath(status.binaryPath, hub.binaryPath ?? undefined)) {
        throw new AppUpdatePreparationError('hub-identity', [{ pid: hub.pid, executable: hub.binaryPath! }], 'Hub identity changed. The update was not started.')
      }
      const stopped = await fetch(new URL('/v1/shutdown', base), {
        method: 'POST', headers: { Authorization: `Bearer ${hub.token}` }, signal
      })
      if (!stopped.ok) throw new Error(`Hub shutdown failed with HTTP ${stopped.status}.`)
    },
    acquireLease: () => acquireAppUpdateLease(options.desktopExecutable, home),
    isAlive: isProcessAlive,
    now: Date.now,
    delay: async (ms) => { await new Promise((resolve) => setTimeout(resolve, ms)) }
  }
}

export async function prepareAppUpdateProcesses(options: PrepareAppUpdateProcessesOptions): Promise<AppUpdateLease> {
  const deps = { ...defaults(options), ...options.dependencies }
  const currentPid = options.currentPid ?? process.pid
  const started = deps.now()
  const timeoutMs = options.timeoutMs ?? 30_000
  const deadline = started + timeoutMs
  const controller = new AbortController()
  let stage = 'lease'
  let blockers: ProcessIdentity[] = []
  const timeoutError = (): AppUpdatePreparationError =>
    new AppUpdatePreparationError(stage, blockers, 'DotCraft processes did not stop before the update deadline. Close them and retry.')
  let timer!: ReturnType<typeof setTimeout>
  const expired = new Promise<never>((_, reject) => {
    timer = setTimeout(() => {
      const error = timeoutError()
      controller.abort(error)
      reject(error)
    }, timeoutMs)
  })
  const run = async <T>(nextStage: string, operation: () => Promise<T>): Promise<T> => {
    stage = nextStage
    if (deps.now() >= deadline) throw timeoutError()
    const result = await Promise.race([operation(), expired])
    if (deps.now() >= deadline) throw timeoutError()
    return result
  }
  const log = (event: string, details: Record<string, unknown> = {}): void =>
    options.log?.(event, { elapsedMs: deps.now() - started, ...details })
  const waitForExit = async (targets: ProcessIdentity[], nextStage: string): Promise<void> => {
    for (;;) {
      blockers = targets.filter(({ pid }) => deps.isAlive(pid))
      if (blockers.length === 0) return
      await run(nextStage, () => deps.delay(Math.min(100, deadline - deps.now())))
    }
  }
  const confirmIdentity = async (target: ProcessIdentity, identify: () => Promise<ProcessIdentity | null>): Promise<boolean> => {
    if (!deps.isAlive(target.pid)) return false
    blockers = [target]
    const live = await run('client-identity', identify)
    if (live?.pid === target.pid && samePath(live.executable, target.executable)) return true
    if (!deps.isAlive(target.pid)) return false
    throw new AppUpdatePreparationError('client-identity', [target], 'Cannot confirm a registered DotCraft process. Close it and retry.')
  }
  let lease: AppUpdateLease | null = null
  try {
    lease = await run('lease', async () => {
      const acquired = await deps.acquireLease()
      if (controller.signal.aborted || deps.now() >= deadline) {
        await acquired.release()
        throw timeoutError()
      }
      return acquired
    })
    log('quiesce')
    await run('quiesce', () => options.quiesce(controller.signal))
    const controls = deps.readControls()
    const clients = new Map<number, ProcessIdentity>()
    for (const entry of controls.desktops) {
      if (entry.pid === currentPid || !samePath(entry.executable, options.desktopExecutable)) continue
      const target = { pid: entry.pid, executable: entry.executable }
      if (!await confirmIdentity(target, () => deps.identifyDesktop(entry))) continue
      clients.set(target.pid, target)
      await run('desktop-stop', () => deps.quitDesktop(entry))
    }
    const tray = controls.tray
    if (tray?.pid && tray.pid !== currentPid && samePath(tray.executable, options.desktopExecutable)) {
      const target = { pid: tray.pid, executable: tray.executable! }
      if (await confirmIdentity(target, () => deps.identifyTray(tray.endpoint!))) {
        clients.set(target.pid, target)
        await run('tray-stop', () => deps.quitTray(tray.endpoint!))
      }
    }
    log('wait-clients', { pids: [...clients.keys()] })
    await waitForExit([...clients.values()], 'clients-exit')
    const hub = deps.readControls().hub
    if (hub && samePath(hub.binaryPath, options.runtimeExecutable) && deps.isAlive(hub.pid)) {
      blockers = [{ pid: hub.pid, executable: hub.binaryPath! }]
      log('hub-stop', { pid: hub.pid })
      try {
        await run('hub-stop', () => deps.stopHub(hub, controller.signal))
      } catch (error) {
        if (error instanceof AppUpdatePreparationError) throw error
        log('hub-shutdown-unavailable', { pid: hub.pid })
      }
      await waitForExit(blockers, 'hub-exit')
    }
    if (deps.now() >= deadline) throw timeoutError()
    log('prepared')
    return lease
  } catch (error) {
    controller.abort(error)
    await lease?.release()
    throw error
  } finally {
    clearTimeout(timer)
  }
}
