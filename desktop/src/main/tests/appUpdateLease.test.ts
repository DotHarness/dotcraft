import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it } from 'vitest'
import { acquireAppUpdateLease, isAppUpdatePreparing, waitForAppUpdatePreparation, type AppUpdateLease } from '../appUpdateLease'

describe('installation preparation lease', () => {
  const homes: string[] = []
  const leases: AppUpdateLease[] = []
  function fixture() {
    const home = mkdtempSync(join(tmpdir(), 'dotcraft-update-lease-'))
    homes.push(home)
    const identity = { pid: process.pid, executable: process.execPath }
    return { home, identity }
  }
  afterEach(() => {
    for (const lease of leases.splice(0)) lease.release()
    for (const home of homes.splice(0)) rmSync(home, { recursive: true, force: true })
  })

  it('blocks another preflight for the same installation and releases idempotently', async () => {
    const f = fixture()
    const first = await acquireAppUpdateLease(f.identity, f.home)
    leases.push(first)
    await expect(isAppUpdatePreparing(process.execPath, f.home)).resolves.toBe(true)
    await expect(acquireAppUpdateLease(f.identity, f.home)).rejects.toThrow('already preparing')
    first.release()
    first.release()
    await expect(isAppUpdatePreparing(process.execPath, f.home)).resolves.toBe(false)
    const replacement = await acquireAppUpdateLease(f.identity, f.home)
    leases.push(replacement)
  })

  it('recovers expired markers instead of permanently blocking future starts', async () => {
    const f = fixture()
    const first = await acquireAppUpdateLease(f.identity, f.home)
    leases.push(first)
    const directory = join(f.home, '.craft', 'desktop', 'updates')
    const file = join(directory, readdirSync(directory)[0])
    const expired = { ...JSON.parse(readFileSync(file, 'utf8')), expiresAt: Date.now() - 1 }
    writeFileSync(file, JSON.stringify(expired))
    await expect(isAppUpdatePreparing(process.execPath, f.home)).resolves.toBe(false)
    const replacement = await acquireAppUpdateLease(f.identity, f.home)
    leases.push(replacement)
    first.release()
    await expect(isAppUpdatePreparing(process.execPath, f.home)).resolves.toBe(true)
  })

  it('waits for an in-progress update rather than letting a new app start backends', async () => {
    const f = fixture()
    const lease = await acquireAppUpdateLease(f.identity, f.home)
    leases.push(lease)
    await expect(waitForAppUpdatePreparation(process.execPath, f.home, 1)).rejects.toThrow('preparing an update')
    const timer = setTimeout(() => lease.release(), 20)
    try {
      await expect(waitForAppUpdatePreparation(process.execPath, f.home, 500)).resolves.toBeUndefined()
    } finally { clearTimeout(timer) }
  })
})
