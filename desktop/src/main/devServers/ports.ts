import { readFile } from 'node:fs/promises'
import { createConnection, createServer } from 'node:net'
import { basename, join } from 'node:path'
import { retargetPortArgs } from './launchFile'

const PROBE_TIMEOUT_MS = 400
const LOOPBACK_HOSTS = ['127.0.0.1', '::1'] as const
const PACKAGE_MANAGERS = new Set(['npm', 'yarn', 'pnpm'])
const PINNED_PORT = /(?:--port[=\s]+\d+|(?:^|\s)-p\s+\d+|\bPORT=\d+)/

function connects(host: string, port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = createConnection({ host, port })
    const done = (result: boolean): void => {
      socket.removeAllListeners()
      socket.destroy()
      resolve(result)
    }
    socket.setTimeout(PROBE_TIMEOUT_MS, () => done(false))
    socket.once('connect', () => done(true))
    socket.once('error', () => done(false))
  })
}

function canBind(host: string, port: number): Promise<boolean> {
  return new Promise((resolve) => {
    const server = createServer()
    server.unref()
    server.once('error', () => resolve(false))
    server.listen({ host, port, exclusive: true }, () => server.close(() => resolve(true)))
  })
}

export async function isListening(port: number): Promise<boolean> {
  const results = await Promise.all(LOOPBACK_HOSTS.map((host) => connects(host, port)))
  return results.some(Boolean)
}

export async function isPortInUse(port: number): Promise<boolean> {
  if (await isListening(port)) return true
  return !(await canBind('127.0.0.1', port))
}

export async function findFreePort(preferred: number, avoid: ReadonlySet<number>): Promise<number | null> {
  for (let port = preferred + 1; port <= Math.min(65535, preferred + 100); port += 1) {
    if (avoid.has(port)) continue
    if (!(await isPortInUse(port))) return port
  }
  return null
}

function packageScript(command: string, args: readonly string[]): string | null {
  if (!PACKAGE_MANAGERS.has(basename(command).replace(/\.(cmd|exe)$/i, '').toLowerCase())) return null
  if (args[0] === 'run' && typeof args[1] === 'string') return args[1]
  return basename(command).toLowerCase().startsWith('yarn') && typeof args[0] === 'string' ? args[0] : null
}

/** Whether the port lives in a package script, where Desktop cannot move it. */
export async function scriptPinsPort(command: string, args: readonly string[], cwd: string): Promise<boolean> {
  const script = packageScript(command, args)
  if (script === null) return false
  try {
    const manifest = JSON.parse(await readFile(join(cwd, 'package.json'), 'utf8')) as { scripts?: Record<string, unknown> }
    const body = manifest.scripts?.[script]
    return typeof body === 'string' && PINNED_PORT.test(body)
  } catch {
    return false
  }
}

export function argsForPort(command: string, args: readonly string[], port: number): string[] {
  const retargeted = retargetPortArgs(args, port)
  if (retargeted) return retargeted
  const script = packageScript(command, args)
  if (script === null) return [...args]
  const npm = basename(command).toLowerCase().startsWith('npm')
  return [...args, ...(npm && !args.includes('--') ? ['--'] : []), '--port', String(port)]
}
