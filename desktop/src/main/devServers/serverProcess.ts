import { spawn, type ChildProcess } from 'node:child_process'
import { stat } from 'node:fs/promises'
import { delimiter, extname, isAbsolute, join, resolve } from 'node:path'

const KILL_GRACE_MS = 3_000

async function isFile(path: string): Promise<boolean> {
  return stat(path).then((info) => info.isFile(), () => false)
}

/** cmd.exe reports a missing command only through its exit code, so Windows checks PATH first. */
export async function commandExists(command: string, cwd: string, env: NodeJS.ProcessEnv): Promise<boolean> {
  if (process.platform !== 'win32') return true
  const extensions = extname(command)
    ? ['']
    : (env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD').split(';').filter(Boolean)
  const candidates = /[\\/]/.test(command) || isAbsolute(command)
    ? [resolve(cwd, command)]
    : [cwd, ...(env.Path ?? env.PATH ?? '').split(delimiter).filter(Boolean)].map((folder) => join(folder, command))
  for (const candidate of candidates) {
    for (const extension of extensions) {
      if (await isFile(candidate + extension)) return true
    }
  }
  return false
}

function quoteWindowsArg(arg: string): string {
  if (arg && !/[\s"&|<>^()%!]/.test(arg)) return arg
  return `"${arg.replace(/(\\*)"/g, '$1$1\\"').replace(/(\\+)$/, '$1$1')}"`
}

/** Runs under cmd.exe on Windows so `.cmd` shims such as npm resolve. */
export function spawnServerProcess(params: {
  command: string
  args: readonly string[]
  cwd: string
  env: NodeJS.ProcessEnv
}): ChildProcess {
  if (process.platform === 'win32') {
    const line = [params.command, ...params.args].map(quoteWindowsArg).join(' ')
    return spawn(process.env.ComSpec || 'cmd.exe', ['/d', '/s', '/c', `"${line}"`], {
      cwd: params.cwd,
      env: params.env,
      windowsHide: true,
      windowsVerbatimArguments: true,
      stdio: ['ignore', 'pipe', 'pipe']
    })
  }
  return spawn(params.command, [...params.args], {
    cwd: params.cwd,
    env: params.env,
    detached: true,
    stdio: ['ignore', 'pipe', 'pipe']
  })
}

function exited(child: ChildProcess): boolean {
  return child.exitCode !== null || child.signalCode !== null
}

export function killProcessTree(child: ChildProcess): Promise<void> {
  if (child.pid === undefined || exited(child)) return Promise.resolve()
  const pid = child.pid
  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      forceKill(pid)
      resolve()
    }, KILL_GRACE_MS)
    child.once('exit', () => {
      clearTimeout(timer)
      if (process.platform !== 'win32') forceKill(pid)
      resolve()
    })
    if (process.platform === 'win32') {
      forceKill(pid)
      return
    }
    try {
      process.kill(-pid, 'SIGTERM')
    } catch {
      clearTimeout(timer)
      resolve()
    }
  })
}

function forceKill(pid: number): void {
  if (process.platform === 'win32') {
    const killer = spawn('taskkill', ['/pid', String(pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' })
    killer.on('error', () => undefined)
    return
  }
  try {
    process.kill(-pid, 'SIGKILL')
  } catch {
    return
  }
}
