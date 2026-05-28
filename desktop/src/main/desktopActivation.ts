import net from 'net'
import { randomBytes } from 'crypto'
import { resolve as resolvePath } from 'path'
import type { BrowserWindow } from 'electron'
import type { WorkspaceActivationEndpoint } from './workspaceLock'

const ACTIVATION_PROTOCOL_VERSION = 1
const ACTIVATION_TIMEOUT_MS = 1200

export interface WorkspaceActivationRequest {
  workspacePath: string
  threadId?: string | null
}

export interface WorkspaceActivationHandle {
  endpoint: WorkspaceActivationEndpoint
  close(): void
}

interface RawActivationMessage {
  type?: unknown
  token?: unknown
  workspacePath?: unknown
  threadId?: unknown
}

function sameWorkspace(a: string, b: string): boolean {
  return resolvePath(a) === resolvePath(b)
}

function writeJsonLine(socket: net.Socket, payload: unknown): void {
  socket.write(JSON.stringify(payload) + '\n', 'utf8')
}

export async function startWorkspaceActivationServer(options: {
  workspacePath: string
  getWindow: () => BrowserWindow | null
  onActivate: (request: WorkspaceActivationRequest) => void
}): Promise<WorkspaceActivationHandle> {
  const token = randomBytes(24).toString('base64url')
  const server = net.createServer((socket) => {
    socket.setEncoding('utf8')
    let buffer = ''
    socket.on('data', (chunk) => {
      buffer += chunk
      let newline = buffer.indexOf('\n')
      while (newline >= 0) {
        const line = buffer.slice(0, newline).trim()
        buffer = buffer.slice(newline + 1)
        if (line) {
          try {
            const message = JSON.parse(line) as RawActivationMessage
            if (message.type !== 'openWorkspace') {
              throw new Error('Unsupported activation request.')
            }
            if (message.token !== token) {
              throw new Error('Invalid activation token.')
            }
            if (typeof message.workspacePath !== 'string' || !sameWorkspace(message.workspacePath, options.workspacePath)) {
              throw new Error('Activation workspace does not match this process.')
            }

            const win = options.getWindow()
            if (!win || win.isDestroyed()) {
              throw new Error('Window is not available.')
            }

            options.onActivate({
              workspacePath: options.workspacePath,
              threadId: typeof message.threadId === 'string' ? message.threadId : null
            })
            writeJsonLine(socket, { ok: true })
          } catch (error) {
            writeJsonLine(socket, {
              ok: false,
              error: error instanceof Error ? error.message : String(error)
            })
          }
        }
        newline = buffer.indexOf('\n')
      }
    })
    socket.on('error', () => {
      socket.destroy()
    })
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
    server.listen(0, '127.0.0.1')
  })

  const address = server.address()
  if (!address || typeof address === 'string') {
    server.close()
    throw new Error('Activation server did not bind a TCP address.')
  }

  return {
    endpoint: {
      host: '127.0.0.1',
      port: address.port,
      token,
      protocolVersion: ACTIVATION_PROTOCOL_VERSION
    },
    close() {
      server.close()
    }
  }
}

export async function requestWorkspaceActivation(
  endpoint: WorkspaceActivationEndpoint,
  request: WorkspaceActivationRequest
): Promise<boolean> {
  if (!endpoint.host || !endpoint.port || !endpoint.token) {
    return false
  }

  return await new Promise<boolean>((resolve) => {
    const socket = net.createConnection({
      host: endpoint.host,
      port: endpoint.port
    })
    let settled = false
    let buffer = ''

    const finish = (ok: boolean): void => {
      if (settled) return
      settled = true
      socket.destroy()
      resolve(ok)
    }

    socket.setEncoding('utf8')
    socket.setTimeout(ACTIVATION_TIMEOUT_MS, () => finish(false))
    socket.on('connect', () => {
      writeJsonLine(socket, {
        type: 'openWorkspace',
        token: endpoint.token,
        workspacePath: request.workspacePath,
        threadId: request.threadId ?? null
      })
    })
    socket.on('data', (chunk) => {
      buffer += chunk
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      const line = buffer.slice(0, newline).trim()
      try {
        const response = JSON.parse(line) as { ok?: unknown }
        finish(response.ok === true)
      } catch {
        finish(false)
      }
    })
    socket.on('error', () => finish(false))
    socket.on('close', () => finish(false))
  })
}
