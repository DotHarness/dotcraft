export const DEV_SERVERS_CHANGED_CHANNEL = 'dev-servers:changed'

export const LAUNCH_FILE_RELATIVE_PATH = '.craft/launch.json'

export type DevServerStatus = 'starting' | 'running' | 'error' | 'stopped'

export type DevServerFailure =
  | { kind: 'spawn'; message: string }
  | { kind: 'exited'; code: number | null; message: string }
  | { kind: 'neverReady'; message: string }
  | { kind: 'portInUse'; port: number; holder: 'sameThread' | 'otherThread' | 'unknown'; message: string }
  | { kind: 'folderLimit'; otherThreads: number; message: string }
  | { kind: 'launchFile'; message: string }

export interface DevServerSnapshot {
  readonly serverId: string
  readonly name: string
  readonly threadId: string
  readonly folder: string
  readonly port: number
  readonly configuredPort?: number
  readonly url: string
  readonly status: DevServerStatus
  readonly startedAt: string
  readonly endedAt?: string
  readonly failure?: DevServerFailure
}

export interface LaunchConfigurationSummary {
  readonly name: string
  readonly port?: number
  readonly url?: string
}

export interface SkippedLaunchConfiguration {
  readonly name: string
  readonly reason: string
}

export type LaunchFileState =
  | { readonly status: 'missing'; readonly path: string }
  | { readonly status: 'invalid'; readonly path: string; readonly reason: string }
  | {
    readonly status: 'ok'
    readonly path: string
    readonly configurations: readonly LaunchConfigurationSummary[]
    readonly skipped: readonly SkippedLaunchConfiguration[]
  }

export interface DevServerList {
  /** The listed folder, spelled as server snapshots spell theirs. */
  readonly folder: string
  readonly launch: LaunchFileState
  readonly servers: readonly DevServerSnapshot[]
}

export interface DevServerScope {
  readonly folder: string
  readonly threadId: string
}

export type DevServerStartResult =
  | { readonly ok: true; readonly server: DevServerSnapshot; readonly reused: boolean }
  | { readonly ok: true; readonly attachUrl: string }
  | { readonly ok: false; readonly failure: DevServerFailure; readonly server?: DevServerSnapshot }

export function devServerUrl(port: number): string {
  return `http://localhost:${port}`
}

export function isActiveDevServer(server: Pick<DevServerSnapshot, 'status'>): boolean {
  return server.status === 'starting' || server.status === 'running'
}

export function devServerPortOfUrl(url: string): number | null {
  try {
    const parsed = new URL(url)
    if (parsed.protocol !== 'http:') return null
    const host = parsed.hostname.replace(/^\[|\]$/g, '').toLowerCase()
    if (host !== 'localhost' && host !== '127.0.0.1' && host !== '::1') return null
    return Number(parsed.port || '80')
  } catch {
    return null
  }
}
