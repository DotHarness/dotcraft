import { spawn, ChildProcess } from 'child_process'
import { EventEmitter } from 'events'
import { Readable, Writable } from 'stream'
import { join, resolve as resolvePath } from 'path'
import { existsSync } from 'fs'
import { execFileSync } from 'child_process'
import type { BinarySource } from './settings'
import { buildDotCraftRuntimeEnv } from './ripgrepRuntime'

export type AppServerManagerEvent =
  | 'started'
  | 'stopped'
  | 'error'
  | 'crash'

export interface AppServerManagerOptions {
  binarySource?: BinarySource
  binaryPath?: string
  workspacePath: string
  listenUrl?: string
  preferDevBuild?: boolean
  requireDevBuild?: boolean
}

export interface ResolvedBinaryInfo {
  source: BinarySource
  path: string | null
}

export interface ResolveBinaryLocationOptions {
  binarySource?: BinarySource
  binaryPath?: string
  preferDevBuild?: boolean
  requireDevBuild?: boolean
}

const DEFAULT_BINARY_SOURCE: BinarySource = 'bundled'

function normalizeBinarySource(source: BinarySource | undefined): BinarySource {
  return source === 'bundled' || source === 'path' || source === 'custom'
    ? source
    : DEFAULT_BINARY_SOURCE
}

function getBinaryFileName(): string {
  return process.platform === 'win32' ? 'dotcraft.exe' : 'dotcraft'
}

function findAncestorWithMarker(startPath: string, marker: string): string | null {
  let current = resolvePath(startPath)
  for (let i = 0; i < 8; i++) {
    if (existsSync(join(current, marker))) {
      return current
    }
    const parent = resolvePath(current, '..')
    if (parent === current) break
    current = parent
  }
  return null
}

function uniquePaths(paths: string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  for (const path of paths) {
    const key = process.platform === 'win32' ? path.toLowerCase() : path
    if (seen.has(key)) continue
    seen.add(key)
    result.push(path)
  }
  return result
}

function resolveDevBuildBinaryPath(): string | null {
  const binaryName = getBinaryFileName()
  const repoRoot = findAncestorWithMarker(__dirname, 'dotcraft.sln')
  const candidates = uniquePaths([
    ...(repoRoot ? [join(repoRoot, 'build', 'release', binaryName)] : []),
    resolvePath(__dirname, '../../../build/dotcraft', binaryName)
  ])

  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      return candidate
    }
  }

  return null
}

function resolveBundledBinaryPath(options: Pick<ResolveBinaryLocationOptions, 'preferDevBuild' | 'requireDevBuild'> = {}): string | null {
  const binaryName = getBinaryFileName()
  if (options.preferDevBuild) {
    const devBuild = resolveDevBuildBinaryPath()
    if (devBuild || options.requireDevBuild) {
      return devBuild
    }
  }

  if (typeof process.resourcesPath === 'string' && process.resourcesPath.length > 0) {
    const packagedResource = join(process.resourcesPath, 'bin', binaryName)
    if (existsSync(packagedResource)) {
      return packagedResource
    }
  }

  const devFallback = resolveDevBuildBinaryPath()
  if (devFallback) {
    return devFallback
  }

  return null
}

function resolvePathBinaryPath(): string | null {
  const whichCommand = process.platform === 'win32' ? 'where' : 'which'
  try {
    const result = execFileSync(whichCommand, ['dotcraft'], { encoding: 'utf8' }).trim()
    const firstLine = result.split(/\r?\n/)[0]?.trim()
    if (firstLine && existsSync(firstLine)) {
      return firstLine
    }
  } catch {
    // Not on PATH.
  }
  return null
}

export function resolveBinaryLocation(options: ResolveBinaryLocationOptions): ResolvedBinaryInfo {
  const source = normalizeBinarySource(options.binarySource)
  const customPath = options.binaryPath?.trim()

  if (source === 'custom') {
    return {
      source,
      path: customPath && existsSync(customPath) ? customPath : null
    }
  }

  if (source === 'path') {
    return {
      source,
      path: resolvePathBinaryPath()
    }
  }

  return {
    source,
    path: resolveBundledBinaryPath(options)
  }
}

/**
 * Manages the DotCraft AppServer subprocess lifecycle.
 * Spawns `dotcraft app-server` (optionally with `--listen`) and manages transport streams.
 * Emits lifecycle events for the Desktop AppServer adapter and Main Process to consume.
 */
export class AppServerManager extends EventEmitter {
  private static readonly STDIO_TO_TERM_GRACE_MS = 700
  private static readonly FORCE_KILL_TIMEOUT_MS = 2200

  private process: ChildProcess | null = null
  private _workspacePath: string
  private _binarySource: BinarySource
  private _binaryPath: string | undefined
  private _listenUrl: string | undefined
  private _preferDevBuild: boolean
  private _requireDevBuild: boolean
  private _shutdownRequested = false
  private _termTimer: ReturnType<typeof setTimeout> | null = null
  private _killTimer: ReturnType<typeof setTimeout> | null = null

  get stdin(): Writable | null {
    return this.process?.stdin ?? null
  }

  get stdout(): Readable | null {
    return this.process?.stdout ?? null
  }

  get isRunning(): boolean {
    return this.process !== null && !this.process.killed && this.process.exitCode === null
  }

  constructor(options: AppServerManagerOptions) {
    super()
    this._workspacePath = options.workspacePath
    this._binarySource = normalizeBinarySource(options.binarySource)
    this._binaryPath = options.binaryPath
    this._listenUrl = options.listenUrl
    this._preferDevBuild = options.preferDevBuild === true
    this._requireDevBuild = options.requireDevBuild === true
  }

  /**
   * Resolves the dotcraft binary path for the selected source.
   */
  private resolveBinary(): string {
    const resolved = resolveBinaryLocation({
      binarySource: this._binarySource,
      binaryPath: this._binaryPath,
      preferDevBuild: this._preferDevBuild,
      requireDevBuild: this._requireDevBuild
    })

    if (resolved.path) {
      return resolved.path
    }

    if (resolved.source === 'custom') {
      const configuredPath = this._binaryPath?.trim()
      throw new Error(
        configuredPath
          ? `Configured DotCraft binary not found: ${configuredPath}`
          : 'Custom DotCraft binary path is empty. Please choose a binary or switch to another source.'
      )
    }

    if (resolved.source === 'path') {
      throw new Error(
        'DotCraft binary not found on PATH. Install dotcraft or switch to the bundled binary in Settings.'
      )
    }

    throw new Error(
      this._preferDevBuild && this._requireDevBuild
        ? 'Local DotCraft build not found. Run build_app.bat from the repository root before starting Desktop dev.'
        : 'Bundled DotCraft binary not found. Reinstall DotCraft or switch to another binary source in Settings.'
    )
  }

  resolveConfiguredBinary(): ResolvedBinaryInfo {
    return resolveBinaryLocation({
      binarySource: this._binarySource,
      binaryPath: this._binaryPath,
      preferDevBuild: this._preferDevBuild,
      requireDevBuild: this._requireDevBuild
    })
  }

  /**
   * Spawns the AppServer subprocess.
   * Emits 'started' on success, 'error' if binary is not found.
   */
  spawn(): void {
    this._shutdownRequested = false
    this.clearShutdownTimers()

    let binaryPath: string
    try {
      binaryPath = this.resolveBinary()
    } catch (err) {
      this.emit('error', err instanceof Error ? err : new Error(String(err)))
      return
    }

    const args = ['app-server']
    if (this._listenUrl) {
      args.push('--listen', this._listenUrl)
    }
    const useStdio = !this._listenUrl || this._listenUrl.startsWith('ws+stdio://')
    const proc = spawn(binaryPath, args, {
      cwd: this._workspacePath,
      env: { ...process.env, ...buildDotCraftRuntimeEnv() },
      stdio: useStdio ? ['pipe', 'pipe', 'inherit'] : ['ignore', 'inherit', 'inherit'],
      windowsHide: true
    })

    this.process = proc

    proc.on('spawn', () => {
      this.emit('started')
    })

    proc.on('error', (err) => {
      if (!this._shutdownRequested) {
        this.emit('error', err)
      }
    })

    proc.on('exit', (code, signal) => {
      this.process = null
      this.clearShutdownTimers()
      if (this._shutdownRequested) {
        this.emit('stopped')
      } else {
        this.emit('crash', { code, signal })
      }
    })
  }

  private clearShutdownTimers(): void {
    if (this._termTimer) {
      clearTimeout(this._termTimer)
      this._termTimer = null
    }
    if (this._killTimer) {
      clearTimeout(this._killTimer)
      this._killTimer = null
    }
  }

  private tryKill(signal: NodeJS.Signals): void {
    if (!this.process || this.process.killed) {
      return
    }
    try {
      this.process.kill(signal)
    } catch {
      // Process already gone
    }
  }

  private scheduleForceKill(afterMs: number): void {
    this._killTimer = setTimeout(() => {
      this.tryKill('SIGKILL')
    }, afterMs)
  }

  /**
   * Gracefully shuts down the AppServer.
   * Closes stdin (sends EOF) when a pipe exists (stdio / ws+stdio).
   * When stdin is not piped (pure WebSocket listen mode), sends SIGTERM instead.
   * Escalates to SIGTERM/SIGKILL with short deadlines to avoid desktop-exit lag.
   */
  shutdown(): void {
    if (!this.process || this._shutdownRequested) {
      return
    }
    this._shutdownRequested = true
    this.clearShutdownTimers()

    try {
      if (this.process.stdin) {
        this.process.stdin.end()
        this._termTimer = setTimeout(() => {
          this.tryKill('SIGTERM')
        }, AppServerManager.STDIO_TO_TERM_GRACE_MS)
      } else {
        this.tryKill('SIGTERM')
      }
    } catch {
      // stdin may already be closed
    }

    this.scheduleForceKill(AppServerManager.FORCE_KILL_TIMEOUT_MS)
  }

  /**
   * Updates the workspace path for future spawns (e.g. workspace switching).
   */
  setWorkspacePath(workspacePath: string): void {
    this._workspacePath = workspacePath
  }

  /**
   * Updates the binary source (e.g. from Settings).
   */
  setBinarySource(binarySource: BinarySource): void {
    this._binarySource = normalizeBinarySource(binarySource)
  }

  /**
   * Updates the binary path (e.g. from Settings).
   */
  setBinaryPath(binaryPath: string): void {
    this._binaryPath = binaryPath
  }

  /**
   * Updates the listen URL for future spawns.
   */
  setListenUrl(listenUrl: string | undefined): void {
    this._listenUrl = listenUrl
  }
}
