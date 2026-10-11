import { stat } from 'fs/promises'
import * as path from 'path'
import { spawn, type ChildProcess, type SpawnOptions } from 'child_process'
import type { OpenTargetLocation } from '../../shared/openTargets'
import type { CustomOpenTarget } from './customTargets'
import type { BuiltInOpenTarget, LaunchPath } from './detection'

export interface LaunchCommand {
  command: string
  args: string[]
  stdin?: string
}

export async function resolveLaunchPath(targetPath: string): Promise<LaunchPath> {
  const resolved = path.resolve(targetPath)
  const isDirectory = await stat(resolved).then((entry) => entry.isDirectory(), () => false)
  return { path: resolved, directory: isDirectory ? resolved : path.dirname(resolved), isDirectory }
}

export function composeBuiltInLaunch(
  target: BuiltInOpenTarget,
  launchPath: LaunchPath,
  location: OpenTargetLocation | null
): LaunchCommand {
  return { command: target.command, args: target.args(launchPath, location) }
}

export function composeCustomLaunch(
  target: CustomOpenTarget,
  launchPath: LaunchPath,
  location: OpenTargetLocation | null
): LaunchCommand {
  if (target.input === 'path') {
    return { command: target.command, args: [...target.args, launchPath.path] }
  }
  const payload = JSON.stringify({ target: target.id, path: launchPath.path, location })
  return target.input === 'jsonArgument'
    ? { command: target.command, args: [...target.args, payload] }
    : { command: target.command, args: [...target.args], stdin: payload }
}

export function runLaunchCommand(launch: LaunchCommand, cwd: string): Promise<void> {
  const stdio: SpawnOptions['stdio'] = launch.stdin === undefined ? 'ignore' : ['pipe', 'ignore', 'ignore']
  const isWindowsShellScript = process.platform === 'win32' && /\.(cmd|bat)$/i.test(launch.command)
  const options: SpawnOptions = isWindowsShellScript
    ? { detached: true, stdio, cwd, shell: true, windowsVerbatimArguments: true }
    : { detached: true, stdio, cwd }
  return new Promise((resolve, reject) => {
    let child: ChildProcess
    try {
      child = spawn(
        isWindowsShellScript ? `"${launch.command}"` : launch.command,
        isWindowsShellScript ? launch.args.map(quoteWindowsArg) : launch.args,
        options
      )
    } catch (error) {
      reject(error)
      return
    }
    let settled = false
    const settle = (callback: () => void): void => {
      if (settled) return
      settled = true
      callback()
    }
    child.once('error', (error) => settle(() => reject(error)))
    child.once('spawn', () => settle(() => {
      if (launch.stdin !== undefined && child.stdin) {
        child.stdin.on('error', () => {})
        child.stdin.end(launch.stdin)
      }
      resolve()
    }))
    child.unref()
  })
}

function quoteWindowsArg(arg: string): string {
  if (arg.length === 0) return '""'
  if (!/[\s"]/.test(arg)) return arg
  return `"${arg.replace(/"/g, '\\"')}"`
}
