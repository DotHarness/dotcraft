import { randomBytes } from 'crypto'
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  unlinkSync,
  writeFileSync
} from 'fs'
import net from 'net'
import { homedir } from 'os'
import { dirname, join } from 'path'

const DESKTOP_PROCESS_REGISTRY_VERSION = 1
const DESKTOP_CONTROL_PROTOCOL_VERSION = 1
const DESKTOP_CONTROL_HOST = '127.0.0.1'
const DESKTOP_CONTROL_TIMEOUT_MS = 1_200
const DESKTOP_CONTROL_MAX_MESSAGE_BYTES = 4_096

interface DesktopControlEndpoint {
  host: string
  port: number
  token: string
  protocolVersion: number
}

interface DesktopProcessRegistration {
  version: number
  pid: number
  startedAt: string
  endpoint: DesktopControlEndpoint
}

interface DesktopControlMessage {
  type?: unknown
  token?: unknown
  protocolVersion?: unknown
}

interface DesktopControlResponse {
  ok?: unknown
}

export interface DesktopProcessRegistrationHandle {
  path: string
  endpoint: DesktopControlEndpoint
  release(): void
}

interface DesktopExitResult {
  requested: number
  acknowledged: number
  failed: number
}

function writeJsonLine(socket: net.Socket, payload: unknown): void {
  socket.write(`${JSON.stringify(payload)}\n`, 'utf8')
}

function isProcessAlive(pid: number): boolean {
  if (!Number.isInteger(pid) || pid <= 0) return false
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'EPERM'
  }
}

function normalizeEndpoint(value: unknown): DesktopControlEndpoint | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const endpoint = value as Partial<DesktopControlEndpoint>
  if (
    endpoint.host !== DESKTOP_CONTROL_HOST ||
    typeof endpoint.port !== 'number' ||
    !Number.isInteger(endpoint.port) ||
    endpoint.port <= 0 ||
    endpoint.port > 65_535 ||
    typeof endpoint.token !== 'string' ||
    endpoint.token.length < 16 ||
    endpoint.protocolVersion !== DESKTOP_CONTROL_PROTOCOL_VERSION
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

function normalizeRegistration(value: unknown): DesktopProcessRegistration | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const registration = value as Partial<DesktopProcessRegistration>
  const endpoint = normalizeEndpoint(registration.endpoint)
  if (
    registration.version !== DESKTOP_PROCESS_REGISTRY_VERSION ||
    typeof registration.pid !== 'number' ||
    !Number.isInteger(registration.pid) ||
    registration.pid <= 0 ||
    typeof registration.startedAt !== 'string' ||
    !endpoint
  ) {
    return null
  }
  return {
    version: registration.version,
    pid: registration.pid,
    startedAt: registration.startedAt,
    endpoint
  }
}

function removeFile(path: string): void {
  try {
    unlinkSync(path)
  } catch {
    // Best-effort cleanup for stale or already removed registry entries.
  }
}

export function getDesktopProcessRegistryDirectory(home = homedir()): string {
  return join(home, '.craft', 'desktop', 'processes')
}

async function startDesktopControlServer(onQuit: () => void): Promise<{
  endpoint: DesktopControlEndpoint
  close(): void
}> {
  const token = randomBytes(24).toString('base64url')
  let quitting = false
  const server = net.createServer((socket) => {
    socket.setEncoding('utf8')
    socket.setTimeout(DESKTOP_CONTROL_TIMEOUT_MS, () => socket.destroy())
    let buffer = ''
    socket.on('data', (chunk) => {
      buffer += chunk
      if (Buffer.byteLength(buffer, 'utf8') > DESKTOP_CONTROL_MAX_MESSAGE_BYTES) {
        socket.destroy()
        return
      }
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      const line = buffer.slice(0, newline).trim()
      buffer = ''
      try {
        const message = JSON.parse(line) as DesktopControlMessage
        if (
          message.type !== 'quit' ||
          message.token !== token ||
          message.protocolVersion !== DESKTOP_CONTROL_PROTOCOL_VERSION
        ) {
          throw new Error('Invalid Desktop control request.')
        }
        if (!quitting) {
          quitting = true
          onQuit()
        }
        writeJsonLine(socket, { ok: true })
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
    server.listen(0, DESKTOP_CONTROL_HOST)
  })

  const address = server.address()
  if (!address || typeof address === 'string') {
    server.close()
    throw new Error('Desktop control server did not bind a TCP address.')
  }

  return {
    endpoint: {
      host: DESKTOP_CONTROL_HOST,
      port: address.port,
      token,
      protocolVersion: DESKTOP_CONTROL_PROTOCOL_VERSION
    },
    close: () => server.close()
  }
}

export async function registerDesktopProcess(
  onQuit: () => void,
  registryDirectory = getDesktopProcessRegistryDirectory()
): Promise<DesktopProcessRegistrationHandle> {
  const control = await startDesktopControlServer(onQuit)
  const instanceId = randomBytes(8).toString('hex')
  const path = join(registryDirectory, `${process.pid}-${instanceId}.json`)
  const temporaryPath = `${path}.tmp`
  try {
    mkdirSync(dirname(path), { recursive: true })
    writeFileSync(temporaryPath, JSON.stringify({
      version: DESKTOP_PROCESS_REGISTRY_VERSION,
      pid: process.pid,
      startedAt: new Date().toISOString(),
      endpoint: control.endpoint
    } satisfies DesktopProcessRegistration, null, 2), 'utf8')
    renameSync(temporaryPath, path)
  } catch (error) {
    control.close()
    removeFile(temporaryPath)
    throw error
  }

  let released = false
  return {
    path,
    endpoint: control.endpoint,
    release() {
      if (released) return
      released = true
      control.close()
      removeFile(path)
    }
  }
}

async function requestDesktopQuit(endpoint: DesktopControlEndpoint): Promise<boolean> {
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
    socket.setTimeout(DESKTOP_CONTROL_TIMEOUT_MS, () => finish(false))
    socket.on('connect', () => {
      writeJsonLine(socket, {
        type: 'quit',
        token: endpoint.token,
        protocolVersion: endpoint.protocolVersion
      })
    })
    socket.on('data', (chunk) => {
      buffer += chunk
      if (Buffer.byteLength(buffer, 'utf8') > DESKTOP_CONTROL_MAX_MESSAGE_BYTES) {
        finish(false)
        return
      }
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      try {
        const response = JSON.parse(buffer.slice(0, newline)) as DesktopControlResponse
        finish(response.ok === true)
      } catch {
        finish(false)
      }
    })
    socket.on('error', () => finish(false))
    socket.on('close', () => finish(false))
  })
}

function readLiveRegistrations(registryDirectory: string): DesktopProcessRegistration[] {
  if (!existsSync(registryDirectory)) return []
  let files: string[]
  try {
    files = readdirSync(registryDirectory).filter((file) => file.endsWith('.json'))
  } catch {
    return []
  }

  const registrations: DesktopProcessRegistration[] = []
  const seenEndpoints = new Set<string>()
  for (const file of files) {
    const path = join(registryDirectory, file)
    let registration: DesktopProcessRegistration | null = null
    try {
      registration = normalizeRegistration(JSON.parse(readFileSync(path, 'utf8')))
    } catch {
      // Invalid registry records are owned by Desktop and can be discarded.
    }
    if (!registration || !isProcessAlive(registration.pid)) {
      removeFile(path)
      continue
    }
    const endpointKey = `${registration.endpoint.port}:${registration.endpoint.token}`
    if (seenEndpoints.has(endpointKey)) continue
    seenEndpoints.add(endpointKey)
    registrations.push(registration)
  }
  return registrations
}

export async function quitRegisteredDesktopProcesses(
  registryDirectory = getDesktopProcessRegistryDirectory()
): Promise<DesktopExitResult> {
  const registrations = readLiveRegistrations(registryDirectory)
  const outcomes = await Promise.allSettled(
    registrations.map((registration) => requestDesktopQuit(registration.endpoint))
  )
  const acknowledged = outcomes.filter(
    (outcome) => outcome.status === 'fulfilled' && outcome.value
  ).length
  return {
    requested: registrations.length,
    acknowledged,
    failed: registrations.length - acknowledged
  }
}
