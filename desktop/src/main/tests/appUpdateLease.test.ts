import { fork, type ChildProcess } from 'child_process'
import { randomUUID } from 'crypto'
import { once } from 'events'
import { mkdtempSync, rmSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { fileURLToPath } from 'url'
import { build } from 'esbuild'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { acquireAppUpdateLease, waitForAppUpdatePreparation, type AppUpdateLease } from '../appUpdateLease'

const executable = 'C:\\fixtures\\install-a\\Desktop.exe'

describe.skipIf(process.platform !== 'win32')('Windows update preparation ownership', () => {
  let directory: string
  let worker: string
  const children: ChildProcess[] = []
  const leases: AppUpdateLease[] = []

  beforeAll(async () => {
    directory = mkdtempSync(join(tmpdir(), 'update-ownership-'))
    worker = join(directory, 'worker.cjs')
    await build({
      stdin: {
        resolveDir: fileURLToPath(new URL('.', import.meta.url)),
        contents: `
          import { acquireAppUpdateLease } from '../appUpdateLease';
          let lease;
          process.on('message', async command => {
            if (command === 'acquire') {
              try {
                lease = await acquireAppUpdateLease(process.argv[2], process.argv[3]);
                process.send('acquired');
              } catch { process.send('blocked'); }
            } else if (command === 'release') {
              await lease.release();
              lease = null;
              process.send('released');
            }
          });
          process.send('ready');
        `
      },
      outfile: worker, bundle: true, platform: 'node', format: 'cjs', target: 'node20', logLevel: 'silent'
    })
  })

  afterEach(async () => {
    await Promise.all(leases.splice(0).map((lease) => lease.release()))
    for (const child of children.splice(0)) {
      if (child.exitCode !== null || child.signalCode !== null) continue
      const exited = once(child, 'exit')
      child.kill()
      await exited
    }
  })
  afterAll(() => rmSync(directory, { recursive: true, force: true }))

  async function start(home: string): Promise<ChildProcess> {
    const child = fork(worker, [executable, home], { stdio: ['ignore', 'ignore', 'ignore', 'ipc'], windowsHide: true })
    children.push(child)
    expect((await once(child, 'message'))[0]).toBe('ready')
    return child
  }

  async function send(child: ChildProcess, command: string): Promise<unknown> {
    const response = once(child, 'message')
    child.send(command)
    return (await response)[0]
  }

  it('grants only one simultaneous process ownership and lets a loser retry after release', async () => {
    const home = join(tmpdir(), randomUUID())
    const contenders = await Promise.all([start(home), start(home)])
    const results = await Promise.all(contenders.map((child) => send(child, 'acquire')))
    expect(results.filter((result) => result === 'acquired')).toHaveLength(1)
    expect(results.filter((result) => result === 'blocked')).toHaveLength(1)
    await expect(waitForAppUpdatePreparation(executable, home, 100)).rejects.toThrow()
    const winner = results.indexOf('acquired')
    await send(contenders[winner], 'release')
    await expect(send(contenders[1 - winner], 'acquire')).resolves.toBe('acquired')
    await send(contenders[1 - winner], 'release')
    await expect(waitForAppUpdatePreparation(executable, home, 1_000)).resolves.toBeUndefined()
  })

  it('recovers ownership immediately after a holder process exits without releasing it', async () => {
    const home = join(tmpdir(), randomUUID())
    const child = await start(home)
    await expect(send(child, 'acquire')).resolves.toBe('acquired')
    const exited = once(child, 'exit')
    child.kill()
    await exited
    await expect(waitForAppUpdatePreparation(executable, home, 1_000)).resolves.toBeUndefined()
    const replacement = await acquireAppUpdateLease(executable, home)
    leases.push(replacement)
    const firstRelease = replacement.release()
    expect(replacement.release()).toBe(firstRelease)
    await firstRelease
  })

  it('keeps different users and installation directories independent', async () => {
    const home = join(tmpdir(), randomUUID())
    leases.push(await acquireAppUpdateLease(executable, home))
    leases.push(await acquireAppUpdateLease('C:\\fixtures\\install-b\\Desktop.exe', home))
    leases.push(await acquireAppUpdateLease(executable, `${home}-other-user`))
    await expect(acquireAppUpdateLease(executable, home)).rejects.toThrow()
  })
})
