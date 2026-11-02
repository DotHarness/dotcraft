import { spawn, type ChildProcess } from 'child_process'
import { once } from 'events'
import { describe, expect, it } from 'vitest'
import { prepareAppUpdateProcesses } from '../appUpdateProcesses'

async function startFixture(exits: boolean): Promise<ChildProcess> {
  const child = spawn(process.execPath, ['-e', `
    process.on('message', message => {
      if (message === 'quit' && ${exits}) setTimeout(() => process.exit(0), 150);
    });
    process.send('ready');
    setInterval(() => {}, 1000);
  `], { stdio: ['ignore', 'ignore', 'ignore', 'ipc'], windowsHide: true })
  await once(child, 'message')
  return child
}

async function closeFixture(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return
  const exited = once(child, 'exit')
  child.kill()
  await exited
}

function options(child: ChildProcess, timeoutMs: number) {
  return {
    desktopExecutable: process.execPath,
    timeoutMs,
    quiesce: async () => {},
    dependencies: {
      readControls: () => ({
        desktops: [{ version: 1, pid: child.pid!, executable: process.execPath,
          startedAt: new Date().toISOString(),
          endpoint: { host: '127.0.0.1', port: 1, token: 'fixture-control', protocolVersion: 1 } }],
        tray: null, hub: null
      }),
      acquireLease: async () => ({ release: async () => {} }),
      identifyDesktop: async () => ({ pid: child.pid!, executable: process.execPath }),
      quitDesktop: async () => { child.send('quit'); return true }
    }
  }
}

describe('registered process exit integration', () => {
  it('waits beyond a quit acknowledgement for a real process to exit before installer handoff', async () => {
    const child = await startFixture(true)
    try {
      const prepared = await prepareAppUpdateProcesses(options(child, 2_000))
      if (child.exitCode === null) await once(child, 'exit')
      expect(child.exitCode).toBe(0)
      await prepared.release()
    } finally { await closeFixture(child) }
  })

  it('aborts preparation without killing a real process that remains alive after acknowledging quit', async () => {
    const child = await startFixture(false)
    try {
      await expect(prepareAppUpdateProcesses(options(child, 100))).rejects.toMatchObject({ stage: 'clients-exit' })
      expect(child.exitCode).toBeNull()
      expect(child.signalCode).toBeNull()
    } finally { await closeFixture(child) }
  })
})
