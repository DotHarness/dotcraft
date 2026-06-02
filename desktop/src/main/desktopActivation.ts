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

export interface WorkspaceWindowState {
  ok: true
  focused: boolean
  visible: boolean
  minimized: boolean
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

interface RawActivationResponse {
  ok?: unknown
  focused?: unknown
  visible?: unknown
  minimized?: unknown
}

function sameWorkspace(a: string, b: string): boolean {
  return resolvePath(a) === resolvePath(b)
}

function writeJsonLine(socket: net.Socket, payload: unknown): void {
  socket.write(JSON.stringify(payload) + '\n', 'utf8')
}

function readWindowState(win: BrowserWindow): WorkspaceWindowState {
  return {
    ok: true,
    focused: win.isFocused(),
    visible: win.isVisible(),
    minimized: win.isMinimized()
  }
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
            if (message.type !== 'openWorkspace' && message.type !== 'windowState') {
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

            if (message.type === 'windowState') {
              writeJsonLine(socket, readWindowState(win))
              continue
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

async function requestActivationMessage(
  endpoint: WorkspaceActivationEndpoint,
  payload: Record<string, unknown>
): Promise<RawActivationResponse | null> {
  if (!endpoint.host || !endpoint.port || !endpoint.token) {
    return null
  }

  return await new Promise<RawActivationResponse | null>((resolve) => {
    const socket = net.createConnection({
      host: endpoint.host,
      port: endpoint.port
    })
    let settled = false
    let buffer = ''

    const finish = (response: RawActivationResponse | null): void => {
      if (settled) return
      settled = true
      socket.destroy()
      resolve(response)
    }

    socket.setEncoding('utf8')
    socket.setTimeout(ACTIVATION_TIMEOUT_MS, () => finish(null))
    socket.on('connect', () => {
      writeJsonLine(socket, {
        ...payload,
        token: endpoint.token
      })
    })
    socket.on('data', (chunk) => {
      buffer += chunk
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      const line = buffer.slice(0, newline).trim()
      try {
        finish(JSON.parse(line) as RawActivationResponse)
      } catch {
        finish(null)
      }
    })
    socket.on('error', () => finish(null))
    socket.on('close', () => finish(null))
  })
}

export async function requestWorkspaceActivation(
  endpoint: WorkspaceActivationEndpoint,
  request: WorkspaceActivationRequest
): Promise<boolean> {
  const response = await requestActivationMessage(endpoint, {
    type: 'openWorkspace',
    workspacePath: request.workspacePath,
    threadId: request.threadId ?? null
  })
  return response?.ok === true
}

export async function requestWorkspaceWindowState(
  endpoint: WorkspaceActivationEndpoint,
  workspacePath: string
): Promise<WorkspaceWindowState | null> {
  const response = await requestActivationMessage(endpoint, {
    type: 'windowState',
    workspacePath
  })
  if (
    response?.ok === true &&
    typeof response.focused === 'boolean' &&
    typeof response.visible === 'boolean' &&
    typeof response.minimized === 'boolean'
  ) {
    return {
      ok: true,
      focused: response.focused,
      visible: response.visible,
      minimized: response.minimized
    }
  }
  return null
}
