import { createHash, randomBytes } from 'crypto'
import { mkdirSync, readFileSync, statSync, unlinkSync, writeFileSync } from 'fs'
import { homedir } from 'os'
import { dirname, join, resolve } from 'path'

const LEASE_LIFETIME_MS = 60_000

interface UpdateLeaseRecord {
  generation: string
  pid: number
  expiresAt: number
}

export interface AppUpdateLease {
  release(): void
}

function leasePath(desktopExecutable: string, homeDir = homedir()): string {
  const installation = resolve(dirname(desktopExecutable)).toLowerCase()
  const key = createHash('sha256').update(installation).digest('hex').slice(0, 24)
  return join(homeDir, '.craft', 'desktop', 'updates', `${key}.json`)
}

function read(path: string): { record: UpdateLeaseRecord | null; raw: string | null } {
  let raw: string
  try { raw = readFileSync(path, 'utf8') }
  catch { return { record: null, raw: null } }
  try {
    const value = JSON.parse(raw) as Partial<UpdateLeaseRecord>
    if (typeof value.generation !== 'string' || typeof value.pid !== 'number'
      || !Number.isInteger(value.pid) || value.pid <= 0
      || typeof value.expiresAt !== 'number' || !Number.isFinite(value.expiresAt)) return { record: null, raw }
    return { record: value as UpdateLeaseRecord, raw }
  } catch {
    return { record: null, raw }
  }
}

function removeUnchanged(path: string, raw: string): void {
  if (read(path).raw !== raw) return
  try { unlinkSync(path) } catch {}
}

function ownerAlive(pid: number): boolean {
  try { process.kill(pid, 0); return true }
  catch (error) { return (error as NodeJS.ErrnoException).code === 'EPERM' }
}

export async function isAppUpdatePreparing(desktopExecutable: string, homeDir = homedir()): Promise<boolean> {
  const path = leasePath(desktopExecutable, homeDir)
  const { record, raw } = read(path)
  if (raw === null) return false
  if (!record) {
    try {
      if (Date.now() - statSync(path).mtimeMs < 5_000) return true
    } catch { return false }
  } else if (record.expiresAt > Date.now() && ownerAlive(record.pid)) {
    return true
  }
  removeUnchanged(path, raw)
  return false
}

export async function waitForAppUpdatePreparation(
  desktopExecutable: string,
  homeDir = homedir(),
  timeoutMs = 40_000
): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (await isAppUpdatePreparing(desktopExecutable, homeDir)) {
    if (Date.now() >= deadline) throw new Error('Another DotCraft process is preparing an update. Try again when it has finished.')
    await new Promise((resolve) => setTimeout(resolve, 200))
  }
}

export async function acquireAppUpdateLease(
  identity: { pid: number; executable: string },
  homeDir = homedir()
): Promise<AppUpdateLease> {
  const path = leasePath(identity.executable, homeDir)
  const record: UpdateLeaseRecord = {
    generation: randomBytes(16).toString('hex'),
    pid: identity.pid,
    expiresAt: Date.now() + LEASE_LIFETIME_MS
  }
  mkdirSync(dirname(path), { recursive: true })
  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      writeFileSync(path, JSON.stringify(record), { flag: 'wx', mode: 0o600 })
      return { release: () => {
        const current = read(path)
        if (current.record?.generation === record.generation && current.raw) removeUnchanged(path, current.raw)
      } }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error
      if (await isAppUpdatePreparing(identity.executable, homeDir))
        throw new Error('Another DotCraft process is already preparing this installation.')
    }
  }
  throw new Error('Could not acquire update preparation ownership.')
}
