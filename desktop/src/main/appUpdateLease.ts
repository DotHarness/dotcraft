import { createHash } from 'crypto'
import net from 'net'
import { homedir } from 'os'
import { win32 } from 'path'

export interface AppUpdateLease {
  release(): Promise<void>
}

function pipeName(desktopExecutable: string, homeDir: string): string {
  if (process.platform !== 'win32') throw new Error('Desktop update preparation is only supported on Windows.')
  const installation = win32.resolve(win32.dirname(desktopExecutable)).toLowerCase()
  const user = win32.resolve(homeDir).toLowerCase()
  const key = createHash('sha256').update(`${user}\0${installation}`).digest('hex').slice(0, 32)
  return `\\\\.\\pipe\\dotcraft-update-${key}`
}

function isPreparing(name: string, timeoutMs: number): Promise<boolean> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(name)
    socket.once('connect', () => { socket.destroy(); resolve(true) })
    socket.once('error', (error: NodeJS.ErrnoException) => {
      socket.destroy()
      if (error.code === 'ENOENT') resolve(false)
      else reject(error)
    })
    socket.setTimeout(timeoutMs, () => {
      socket.destroy()
      reject(new Error('Cannot check whether a Desktop update is being prepared.'))
    })
  })
}

export async function waitForAppUpdatePreparation(
  desktopExecutable: string,
  homeDir = homedir(),
  timeoutMs = 40_000
): Promise<void> {
  const name = pipeName(desktopExecutable, homeDir)
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const remaining = deadline - Date.now()
    if (remaining <= 0) throw new Error('Another DotCraft process is preparing an update. Try again when it has finished.')
    if (!await isPreparing(name, Math.min(1_000, remaining))) return
    await new Promise((resolve) => setTimeout(resolve, Math.min(100, Math.max(0, deadline - Date.now()))))
  }
}

export async function acquireAppUpdateLease(desktopExecutable: string, homeDir = homedir()): Promise<AppUpdateLease> {
  const server = net.createServer((socket) => socket.destroy())
  const name = pipeName(desktopExecutable, homeDir)
  await new Promise<void>((resolve, reject) => {
    const failed = (error: NodeJS.ErrnoException): void => {
      reject(error.code === 'EADDRINUSE' ? new Error('Another DotCraft process is already preparing this installation.') : error)
    }
    server.once('error', failed)
    server.listen(name, () => {
      server.removeListener('error', failed)
      resolve()
    })
  })
  server.unref()
  let releasing: Promise<void> | null = null
  return {
    release: () => releasing ??= new Promise<void>((resolve, reject) => {
      server.close((error) => error ? reject(error) : resolve())
    })
  }
}
