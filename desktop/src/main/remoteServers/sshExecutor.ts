import { spawn } from 'child_process'
import net from 'net'
import { buildSshArgs, type RemoteHost } from '../../shared/remoteServers'

export interface SshRunResult {
  code: number | null
  stdout: string
  stderr: string
  timedOut: boolean
}

export interface SshRunOptions {
  /** ssh ConnectTimeout (seconds). */
  connectTimeoutSec?: number
  /** Hard wall-clock cap on the whole command (ms); the process is killed past it. */
  timeoutMs?: number
  /** Override the ssh executable (defaults to `ssh` on PATH). */
  sshPath?: string
}

/** Injectable runner so the manager can be unit-tested without a real ssh. */
export type SshRunner = (
  host: RemoteHost,
  remoteCommand: string,
  opts?: SshRunOptions
) => Promise<SshRunResult>

const DEFAULT_TIMEOUT_MS = 30_000

/**
 * Run one allow-listed remote command through the system `ssh` binary. The
 * remote command is built by the caller from validated parameters; arguments are
 * passed as an argv vector (never assembled into a local shell string).
 */
export const runSshCommand: SshRunner = (host, remoteCommand, opts = {}) =>
  new Promise((resolve) => {
    const args = buildSshArgs(host, remoteCommand, { connectTimeoutSec: opts.connectTimeoutSec })
    const child = spawn(opts.sshPath ?? 'ssh', args, { windowsHide: true })

    let stdout = ''
    let stderr = ''
    let timedOut = false
    let settled = false

    const timer = setTimeout(() => {
      timedOut = true
      child.kill()
    }, opts.timeoutMs ?? DEFAULT_TIMEOUT_MS)

    child.stdout?.on('data', (chunk) => {
      stdout += chunk.toString()
    })
    child.stderr?.on('data', (chunk) => {
      stderr += chunk.toString()
    })

    const finish = (code: number | null): void => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      resolve({ code, stdout, stderr, timedOut })
    }

    child.on('error', (err) => {
      stderr += `\n${err.message}`
      finish(null)
    })
    child.on('close', (code) => finish(code))
  })

/** Allocate an unused loopback TCP port for a local tunnel. */
export function getFreeLocalPort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = net.createServer()
    server.unref()
    server.on('error', reject)
    server.listen(0, '127.0.0.1', () => {
      const address = server.address()
      if (address && typeof address === 'object') {
        const { port } = address
        server.close(() => resolve(port))
      } else {
        server.close()
        reject(new Error('Could not allocate a local port'))
      }
    })
  })
}

/** Poll until something is listening on a loopback port, or time out. */
export function waitForLocalPort(port: number, timeoutMs = 8000): Promise<boolean> {
  const deadline = Date.now() + timeoutMs
  return new Promise((resolve) => {
    const attempt = (): void => {
      const socket = net.connect(port, '127.0.0.1')
      socket.once('connect', () => {
        socket.destroy()
        resolve(true)
      })
      socket.once('error', () => {
        socket.destroy()
        if (Date.now() > deadline) resolve(false)
        else setTimeout(attempt, 150)
      })
    }
    attempt()
  })
}
