import { mkdirSync, readFileSync, renameSync, writeFileSync } from 'fs'
import { dirname, join } from 'path'
import { app, safeStorage } from 'electron'
import type { RelayInfo } from '@dotcraft/sdk/gateway'
import type { PairedComputerProject } from '../../shared/pairedComputers'

export type StoredProject = Pick<PairedComputerProject, 'projectId' | 'displayName' | 'path' | 'kind'>

export interface StoredComputer {
  computerId: string
  name: string
  deviceId: string
  port: number
  fingerprint: string
  addresses: string[]
  relay: RelayInfo | null
  pairedAt: string
  lastConnectedAt: string | null
  version: string | null
  credential: string
  projects: StoredProject[]
}

export interface PairedComputersStorage {
  load(): StoredComputer[]
  save(computers: StoredComputer[]): void
}

export interface CredentialVault {
  available(): boolean
  seal(credential: string): string
  open(sealed: string): string | null
}

export function createFileStorage(
  filePath: string = join(app.getPath('userData'), 'paired-computers.json')
): PairedComputersStorage {
  return {
    load() {
      try {
        const parsed = JSON.parse(readFileSync(filePath, 'utf8')) as { computers?: StoredComputer[] }
        return Array.isArray(parsed.computers) ? parsed.computers : []
      } catch {
        return []
      }
    },
    save(computers) {
      mkdirSync(dirname(filePath), { recursive: true })
      const temporary = `${filePath}.tmp`
      writeFileSync(temporary, JSON.stringify({ computers }, null, 2), { encoding: 'utf8', mode: 0o600 })
      renameSync(temporary, filePath)
    }
  }
}

export function createSafeStorageVault(): CredentialVault {
  return {
    available() {
      if (!safeStorage.isEncryptionAvailable()) return false
      return process.platform !== 'linux' || safeStorage.getSelectedStorageBackend() !== 'basic_text'
    },
    seal(credential) {
      return safeStorage.encryptString(credential).toString('base64')
    },
    open(sealed) {
      try {
        return safeStorage.decryptString(Buffer.from(sealed, 'base64'))
      } catch {
        return null
      }
    }
  }
}
