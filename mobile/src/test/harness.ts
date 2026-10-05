import type { Timers } from '../core/backoff'
import type { ComputerLink } from '../core/computerLink'
import type { LiveNotifier } from '../core/liveSession'
import { MobileSession } from '../core/session'
import type { ComputerState } from '../core/state'
import type { FakeComputer } from '../demo/fakeComputer'
import { FakeNetwork } from '../demo/fakeNetwork'
import { MemoryCredentials, MemoryStorage, pairedRecord } from '../demo/memory'
import { DEMO_CREDENTIAL } from '../demo/seed'

export class ManualTimers implements Timers {
  private next = 0
  readonly scheduled = new Map<number, { callback: () => void; ms: number }>()

  setTimeout(callback: () => void, ms: number): unknown {
    this.next += 1
    this.scheduled.set(this.next, { callback, ms })
    return this.next
  }

  clearTimeout(handle: unknown): void {
    this.scheduled.delete(handle as number)
  }

  delays(): number[] {
    return [...this.scheduled.values()].map((entry) => entry.ms)
  }

  runAll(): void {
    const entries = [...this.scheduled.entries()]
    this.scheduled.clear()
    for (const [, entry] of entries) entry.callback()
  }

  fire(ms: number): void {
    for (const [handle, entry] of [...this.scheduled.entries()]) {
      if (entry.ms !== ms) continue
      this.scheduled.delete(handle)
      entry.callback()
    }
  }
}

export interface Harness {
  session: MobileSession
  link(computerId?: string): ComputerLink
  computerState(computerId?: string): ComputerState
  network: FakeNetwork
  timers: ManualTimers
  storage: MemoryStorage
  credentials: MemoryCredentials
}

export function createHarness(
  computers: FakeComputer[],
  options: { paired?: number; storage?: MemoryStorage; live?: LiveNotifier } = {},
): Harness {
  const network = new FakeNetwork(computers)
  const timers = new ManualTimers()
  const storage = options.storage ?? new MemoryStorage()
  const paired = computers.slice(0, options.paired ?? 1)
  if (!options.storage) storage.value = paired.length > 0 ? pairedRecord(paired, '2026-09-28T10:00:00.000Z') : null
  const credentials = new MemoryCredentials(Object.fromEntries(paired.map((computer) => [computer.id, DEMO_CREDENTIAL])))
  const session = new MobileSession({
    native: network,
    credentials,
    storage,
    device: { displayName: 'Test Phone', platform: 'android', osVersion: '16', appVersion: '0.8.0' },
    live: options.live,
    timers,
    random: () => 0.5,
  })
  const first = computers[0]?.id ?? ''
  return {
    session,
    link: (computerId = first) => session.computer(computerId)!,
    computerState: (computerId = first) => session.store.getState().computers[computerId],
    network,
    timers,
    storage,
    credentials,
  }
}

export async function waitFor(condition: () => boolean, timeoutMs = 3_000): Promise<void> {
  const started = Date.now()
  while (!condition()) {
    if (Date.now() - started > timeoutMs) throw new Error('Timed out waiting for condition')
    await new Promise((resolve) => setTimeout(resolve, 2))
  }
}
