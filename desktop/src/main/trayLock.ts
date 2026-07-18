import { randomBytes } from 'crypto'
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, unlinkSync, writeSync } from 'fs'
import net from 'net'
import { homedir } from 'os'
import { dirname, join } from 'path'

const TRAY_LOCK_VERSION = 2
const TRAY_CONTROL_PROTOCOL_VERSION = 1
const TRAY_CONTROL_HOST = '127.0.0.1'
const TRAY_CONTROL_TIMEOUT_MS = 1_200
const TRAY_CONTROL_MAX_MESSAGE_BYTES = 4_096
const TRAY_LOCK_MAX_ATTEMPTS = 8

export interface TrayControlEndpoint {
  host: string
  port: number
  token: string
  protocolVersion: number
}

interface TrayLockInfo {
  version?: number
  pid?: number
  startedAt?: string
  endpoint?: TrayControlEndpoint
}

interface TrayControlMessage {
  type?: unknown
  token?: unknown
  protocolVersion?: unknown
}

interface TrayControlResponse {
  ok?: unknown
}

export interface TrayLockHandle {
  path: string
  endpoint: TrayControlEndpoint
  release: () => void
}

export interface TrayLockOptions {
  onShutdown?: () => void
}

interface TrayControlHandle {
  endpoint: TrayControlEndpoint
  close: () => void
}

interface LockSnapshot {
  raw: string
  info: TrayLockInfo | null
}

export function getTrayLockPath(home = homedir()): string {
  return join(home, '.craft', 'desktop', 'tray.lock')
}

function normalizeEndpoint(value: unknown): TrayControlEndpoint | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const endpoint = value as Partial<TrayControlEndpoint>
  if (
    endpoint.host !== TRAY_CONTROL_HOST ||
    typeof endpoint.port !== 'number' ||
    !Number.isInteger(endpoint.port) ||
    endpoint.port <= 0 ||
    endpoint.port > 65_535 ||
    typeof endpoint.token !== 'string' ||
    endpoint.token.length < 16 ||
    endpoint.protocolVersion !== TRAY_CONTROL_PROTOCOL_VERSION
  ) {
    return null
  }
  return {
    host: endpoint.host,
    port: endpoint.port,
    token: endpoint.token,
    protocolVersion: endpoint.protocolVersion
  }
}

function readLockSnapshot(lockPath: string): LockSnapshot | null {
  try {
    const raw = readFileSync(lockPath, 'utf8')
    const parsed = JSON.parse(raw) as TrayLockInfo
    return { raw, info: parsed && typeof parsed === 'object' ? parsed : null }
  } catch {
    if (!existsSync(lockPath)) return null
    try {
      return { raw: readFileSync(lockPath, 'utf8'), info: null }
    } catch {
      return null
    }
  }
}

function endpointFromSnapshot(snapshot: LockSnapshot | null): TrayControlEndpoint | null {
  if (snapshot?.info?.version !== TRAY_LOCK_VERSION) return null
  return normalizeEndpoint(snapshot.info.endpoint)
}

function writeJsonLine(socket: net.Socket, payload: unknown): void {
  socket.write(`${JSON.stringify(payload)}\n`, 'utf8')
}

async function startTrayControlServer(options: TrayLockOptions): Promise<TrayControlHandle> {
  const token = randomBytes(24).toString('base64url')
  let closing = false
  const server = net.createServer((socket) => {
    socket.setEncoding('utf8')
    socket.setTimeout(TRAY_CONTROL_TIMEOUT_MS, () => socket.destroy())
    let buffer = ''
    socket.on('data', (chunk) => {
      buffer += chunk
      if (Buffer.byteLength(buffer, 'utf8') > TRAY_CONTROL_MAX_MESSAGE_BYTES) {
        socket.destroy()
        return
      }
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      const line = buffer.slice(0, newline).trim()
      buffer = ''
      try {
        const message = JSON.parse(line) as TrayControlMessage
        if (
          message.token !== token ||
          message.protocolVersion !== TRAY_CONTROL_PROTOCOL_VERSION ||
          (message.type !== 'ping' && message.type !== 'shutdown')
        ) {
          throw new Error('Invalid tray control request.')
        }
        writeJsonLine(socket, { ok: true })
        if (message.type === 'shutdown' && !closing) {
          closing = true
          setImmediate(() => options.onShutdown?.())
        }
      } catch {
        writeJsonLine(socket, { ok: false })
      } finally {
        socket.end()
      }
    })
    socket.on('error', () => socket.destroy())
  })

  await new Promise<void>((resolve, reject) => {
    const onError = (error: Error): void => {
      server.off('listening', onListening)
      reject(error)
    }
    const onListening = (): void => {
      server.off('error', onError)
      resolve()
    }
    server.once('error', onError)
    server.once('listening', onListening)
    server.listen(0, TRAY_CONTROL_HOST)
  })

  const address = server.address()
  if (!address || typeof address === 'string') {
    server.close()
    throw new Error('Tray control server did not bind a TCP address.')
  }

  return {
    endpoint: {
      host: TRAY_CONTROL_HOST,
      port: address.port,
      token,
      protocolVersion: TRAY_CONTROL_PROTOCOL_VERSION
    },
    close: () => server.close()
  }
}

async function requestTrayControl(
  endpoint: TrayControlEndpoint,
  type: 'ping' | 'shutdown'
): Promise<boolean> {
  return await new Promise<boolean>((resolve) => {
    const socket = net.createConnection({ host: endpoint.host, port: endpoint.port })
    let settled = false
    let buffer = ''
    const finish = (result: boolean): void => {
      if (settled) return
      settled = true
      socket.destroy()
      resolve(result)
    }
    socket.setEncoding('utf8')
    socket.setTimeout(TRAY_CONTROL_TIMEOUT_MS, () => finish(false))
    socket.on('connect', () => {
      writeJsonLine(socket, {
        type,
        token: endpoint.token,
        protocolVersion: endpoint.protocolVersion
      })
    })
    socket.on('data', (chunk) => {
      buffer += chunk
      if (Buffer.byteLength(buffer, 'utf8') > TRAY_CONTROL_MAX_MESSAGE_BYTES) {
        finish(false)
        return
      }
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      try {
        const response = JSON.parse(buffer.slice(0, newline)) as TrayControlResponse
        finish(response.ok === true)
      } catch {
        finish(false)
      }
    })
    socket.on('error', () => finish(false))
    socket.on('close', () => finish(false))
  })
}

function removeSnapshotIfUnchanged(lockPath: string, snapshot: LockSnapshot): boolean {
  const current = readLockSnapshot(lockPath)
  if (!current || current.raw !== snapshot.raw) return false
  try {
    unlinkSync(lockPath)
    return true
  } catch {
    return false
  }
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds))
}

export async function tryAcquireTrayLock(
  lockPath = getTrayLockPath(),
  options: TrayLockOptions = {}
): Promise<TrayLockHandle | null> {
  mkdirSync(dirname(lockPath), { recursive: true })
  const control = await startTrayControlServer(options)

  for (let attempt = 0; attempt < TRAY_LOCK_MAX_ATTEMPTS; attempt++) {
    try {
      const fd = openSync(lockPath, 'wx')
      try {
        writeSync(fd, JSON.stringify({
          version: TRAY_LOCK_VERSION,
          pid: process.pid,
          startedAt: new Date().toISOString(),
          endpoint: control.endpoint
        }, null, 2), 0, 'utf8')
      } finally {
        closeSync(fd)
      }

      let released = false
      return {
        path: lockPath,
        endpoint: control.endpoint,
        release: () => {
          if (released) return
          released = true
          control.close()
          const snapshot = readLockSnapshot(lockPath)
          const endpoint = endpointFromSnapshot(snapshot)
          if (endpoint?.token !== control.endpoint.token) return
          try {
            unlinkSync(lockPath)
          } catch {
            // Best-effort cleanup; failed probes will recover a stale discovery file.
          }
        }
      }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') {
        control.close()
        throw error
      }
    }

    const snapshot = readLockSnapshot(lockPath)
    const endpoint = endpointFromSnapshot(snapshot)
    if (endpoint && await requestTrayControl(endpoint, 'ping')) {
      control.close()
      return null
    }

    if (snapshot) {
      // Give a concurrent winner time to publish its endpoint, then verify that
      // this is still the same failed generation before removing it.
      await delay(10 + attempt * 5)
      const current = readLockSnapshot(lockPath)
      const currentEndpoint = endpointFromSnapshot(current)
      if (currentEndpoint && await requestTrayControl(currentEndpoint, 'ping')) {
        control.close()
        return null
      }
      if (current?.raw === snapshot.raw) {
        removeSnapshotIfUnchanged(lockPath, snapshot)
      }
    }
  }

  control.close()
  return null
}

export async function requestTrayShutdown(lockPath = getTrayLockPath()): Promise<boolean> {
  const endpoint = endpointFromSnapshot(readLockSnapshot(lockPath))
  if (!endpoint) return false
  return await requestTrayControl(endpoint, 'shutdown')
}
