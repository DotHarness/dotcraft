import { spawn, type ChildProcess } from 'child_process'
import type { TunnelInfo } from '../../shared/dockerDeployments'
import { buildSshTunnelArgs, type SshTarget } from '../../shared/sshMachines'
import { redactSecrets } from '../../shared/sshShell'
import { buildSshSpawnEnv, getFreeLocalPort, waitForLocalPort } from './sshExecutor'

interface ActiveTunnel {
  ownerId: string
  slot: string
  remotePort: number
  proc: ChildProcess
  info: TunnelInfo
  closedByUs: boolean
}

export interface TunnelOpenOptions {
  onUnexpectedClose?: () => void
}

export interface Tunnels {
  open(target: SshTarget, ownerId: string, slot: string, remotePort: number, options?: TunnelOpenOptions): Promise<TunnelInfo>
  get(ownerId: string, slot: string): TunnelInfo | undefined
  closeOne(ownerId: string, slot: string): void
  closeMatching(ownerId: string, slotPrefix: string): void
  closeForOwner(ownerId: string): void
  closeAll(): void
}

function key(ownerId: string, slot: string): string {
  return `${ownerId}::${slot}`
}

function isAlive(proc: ChildProcess): boolean {
  return !proc.killed && proc.exitCode == null && proc.signalCode == null
}

export class TunnelManager implements Tunnels {
  private readonly tunnels = new Map<string, ActiveTunnel>()

  constructor(private readonly sshPath: string = 'ssh') {}

  async open(
    target: SshTarget,
    ownerId: string,
    slot: string,
    remotePort: number,
    options: TunnelOpenOptions = {}
  ): Promise<TunnelInfo> {
    const k = key(ownerId, slot)
    const existing = this.tunnels.get(k)
    if (existing && isAlive(existing.proc) && existing.remotePort === remotePort) {
      return existing.info
    }
    if (existing) this.closeOne(ownerId, slot)

    const localPort = await getFreeLocalPort()
    const proc = spawn(this.sshPath, buildSshTunnelArgs(target, localPort, remotePort), {
      windowsHide: true,
      env: buildSshSpawnEnv()
    })

    let stderr = ''
    proc.stderr?.on('data', (chunk) => {
      stderr += chunk.toString()
    })

    const info: TunnelInfo = { localPort, localUrl: `127.0.0.1:${localPort}` }
    const tunnel: ActiveTunnel = { ownerId, slot, remotePort, proc, info, closedByUs: false }
    proc.on('close', () => {
      if (this.tunnels.get(k) === tunnel) this.tunnels.delete(k)
      if (!tunnel.closedByUs) options.onUnexpectedClose?.()
    })
    proc.on('error', () => {})

    const ready = await waitForLocalPort(localPort)
    if (!ready || !isAlive(proc)) {
      tunnel.closedByUs = true
      proc.kill()
      throw new Error(redactSecrets(stderr.trim()) || 'SSH forward failed to establish')
    }

    this.tunnels.set(k, tunnel)
    return info
  }

  get(ownerId: string, slot: string): TunnelInfo | undefined {
    return this.tunnels.get(key(ownerId, slot))?.info
  }

  closeOne(ownerId: string, slot: string): void {
    const k = key(ownerId, slot)
    const tunnel = this.tunnels.get(k)
    if (!tunnel) return
    this.tunnels.delete(k)
    tunnel.closedByUs = true
    tunnel.proc.kill()
  }

  closeMatching(ownerId: string, slotPrefix: string): void {
    for (const tunnel of [...this.tunnels.values()]) {
      if (tunnel.ownerId === ownerId && tunnel.slot.startsWith(slotPrefix)) this.closeOne(ownerId, tunnel.slot)
    }
  }

  closeForOwner(ownerId: string): void {
    for (const tunnel of [...this.tunnels.values()]) {
      if (tunnel.ownerId === ownerId) this.closeOne(ownerId, tunnel.slot)
    }
  }

  closeAll(): void {
    for (const tunnel of [...this.tunnels.values()]) this.closeOne(tunnel.ownerId, tunnel.slot)
  }
}
