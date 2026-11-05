import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { resolve } from 'node:path'
import { homedir } from 'node:os'
import { hubLockPath, readHubLockFromPath, type HubLockInfo } from '@dotcraft/sdk/hub'

const execute = promisify(execFile)

export interface PrepareAppUpdateProcessesOptions {
  runtimeExecutable?: string
  homeDir?: string
  readHub?: () => HubLockInfo | null
  stopHub?: (executable: string, hub: HubLockInfo) => Promise<void>
}

function sameExecutable(left: string | undefined | null, right: string): boolean {
  if (!left) return false
  const normalize = (value: string): string =>
    process.platform === 'win32' ? resolve(value).toLowerCase() : resolve(value)
  return normalize(left) === normalize(right)
}

export async function stopBundledHub(executable: string, hub: HubLockInfo): Promise<void> {
  if (!hub.startedAt || !hub.binaryPath)
    throw new Error('Cannot confirm the bundled Hub process identity before update.')
  await execute(
    executable,
    [
      'hub',
      'stop',
      '--expected-pid',
      String(hub.pid),
      '--expected-started-at',
      hub.startedAt,
      '--expected-executable',
      hub.binaryPath,
      '--grace-ms',
      '10000'
    ],
    { windowsHide: true, timeout: 20_000, maxBuffer: 16_384 }
  )
}

export async function prepareAppUpdateProcesses(options: PrepareAppUpdateProcessesOptions): Promise<void> {
  if (!options.runtimeExecutable) return
  const hub = (options.readHub ?? (() => readHubLockFromPath(hubLockPath(options.homeDir ?? homedir()))))()
  if (!hub || !sameExecutable(hub.binaryPath, options.runtimeExecutable)) return
  await (options.stopHub ?? stopBundledHub)(options.runtimeExecutable, hub)
}
