import { spawn, type ChildProcess } from 'child_process'
import { buildSshTunnelArgs, type RemoteHost, type TunnelInfo } from '../../shared/remoteServers'
import { getFreeLocalPort, waitForLocalPort } from './sshExecutor'

export type TunnelKind = 'appserver' | 'dashboard'

interface ActiveTunnel {
  hostId: string
  stackId: string
  kind: TunnelKind
  remotePort: number
  proc: ChildProcess
  info: TunnelInfo
}

function key(hostId: string, stackId: string, kind: TunnelKind): string {
  return `${hostId}::${stackId}::${kind}`
}

/**
 * Owns the lifecycle of local SSH `-L` forwards. Tunnels are loopback-only and
 * are torn down on disconnect, stack/host removal, and app quit so a stale
 * forward never outlives its connection.
 */
export class TunnelManager {
  private readonly tunnels = new Map<string, ActiveTunnel>()

  constructor(private readonly sshPath: string = 'ssh') {}

  /** Open (or reuse) a tunnel for a stack's remote port and return the local endpoint. */
  async open(
    host: RemoteHost,
    stackId: string,
    remotePort: number,
    kind: TunnelKind
  ): Promise<TunnelInfo> {
    const existing = this.tunnels.get(key(host.id, stackId, kind))
    if (existing && !existing.proc.killed) {
      return existing.info
    }

    const localPort = await getFreeLocalPort()
    const args = buildSshTunnelArgs(host, localPort, remotePort)
    const proc = spawn(this.sshPath, args, { windowsHide: true })

    let stderr = ''
    proc.stderr?.on('data', (chunk) => {
      stderr += chunk.toString()
    })

    const k = key(host.id, stackId, kind)
    proc.on('close', () => {
      this.tunnels.delete(k)
    })

    const ready = await waitForLocalPort(localPort)
    if (!ready) {
      proc.kill()
      throw new Error(stderr.trim() || 'SSH tunnel failed to establish')
    }

    const info: TunnelInfo = { localPort, localUrl: `127.0.0.1:${localPort}` }
    this.tunnels.set(k, { hostId: host.id, stackId, kind, remotePort, proc, info })
    return info
  }

  get(hostId: string, stackId: string, kind: TunnelKind): TunnelInfo | undefined {
    return this.tunnels.get(key(hostId, stackId, kind))?.info
  }

  closeOne(hostId: string, stackId: string, kind: TunnelKind): void {
    const k = key(hostId, stackId, kind)
    const tunnel = this.tunnels.get(k)
    if (tunnel) {
      tunnel.proc.kill()
      this.tunnels.delete(k)
    }
  }

  closeForStack(hostId: string, stackId: string): void {
    for (const [k, tunnel] of this.tunnels) {
      if (tunnel.hostId === hostId && tunnel.stackId === stackId) {
        tunnel.proc.kill()
        this.tunnels.delete(k)
      }
    }
  }

  closeForHost(hostId: string): void {
    for (const [k, tunnel] of this.tunnels) {
      if (tunnel.hostId === hostId) {
        tunnel.proc.kill()
        this.tunnels.delete(k)
      }
    }
  }

  closeAll(): void {
    for (const [, tunnel] of this.tunnels) {
      tunnel.proc.kill()
    }
    this.tunnels.clear()
  }
}
