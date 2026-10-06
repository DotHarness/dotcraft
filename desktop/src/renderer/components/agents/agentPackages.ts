import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

export const AGENT_PACKAGE_MAX_BYTES = 64 * 1024 * 1024
const CHUNK_BYTES = 1024 * 1024

export type AgentPackageKind = 'skill' | 'plugin'
export type AgentImportPackageState = 'installed' | 'bundled' | 'marketplace' | 'addMarketplace' | 'unavailable'

export type AgentPackageRef = {
  kind: AgentPackageKind
  name: string
}

export type AgentImportPackage = AgentPackageRef & {
  displayName: string
  version?: string | null
  dotnet: boolean
  state: AgentImportPackageState
  installedVersion?: string | null
  marketplaceName?: string | null
  reason?: 'localMarketplace' | 'notOffered' | null
}

export type AgentImportUnresolved = {
  skills: string[]
  mcpServers: string[]
  plugins: string[]
}

export type AgentImportPreview = {
  importId: string
  kind: 'package' | 'markdown'
  name: string
  description?: string | null
  nameTaken: boolean
  problems: string[]
  packages: AgentImportPackage[]
  unresolved: AgentImportUnresolved
}

export type AgentExportPackage = AgentPackageRef & {
  displayName: string
  version?: string | null
  dotnet: boolean
  bytes: number
  marketplaceName?: string | null
  reasons: string[]
}

export type AgentExportPlan = {
  fileName: string
  maximumBytes: number
  packages: AgentExportPackage[]
}

export type AgentImportCommit = {
  importId: string
  name: string
  description: string
  source: 'user' | 'workspace'
  packages: AgentPackageRef[]
}

export function installable(pkg: AgentImportPackage): boolean {
  return pkg.state === 'bundled' || pkg.state === 'marketplace' || pkg.state === 'addMarketplace'
}

export function packageKey(pkg: AgentPackageRef): string {
  return `${pkg.kind}|${pkg.name}`
}

async function rpc<T, M extends keyof ClientRequestMethods = keyof ClientRequestMethods>(
  method: M,
  params: ClientRequestMethods[M]['params']
): Promise<T> {
  return (await window.api.appServer.sendRequest(method, params)) as T
}

function toBase64(bytes: Uint8Array): string {
  let binary = ''
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000))
  return btoa(binary)
}

function fromBase64(data: string): Uint8Array {
  const binary = atob(data)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

export async function uploadAgentPackage(file: File): Promise<AgentImportPreview> {
  const bytes = new Uint8Array(await file.arrayBuffer())
  let importId: string | undefined
  for (let offset = 0; ; offset += CHUNK_BYTES) {
    const chunk = bytes.subarray(offset, offset + CHUNK_BYTES)
    const result = await rpc<{ importId: string; receivedBytes: number; preview?: AgentImportPreview }>('agent/profiles/import/upload', {
      importId,
      fileName: file.name,
      totalBytes: bytes.length,
      offset,
      dataBase64: toBase64(chunk)
    })
    importId = result.importId
    if (result.preview) return result.preview
    if (offset + CHUNK_BYTES >= bytes.length) throw new Error('The server did not return a preview.')
  }
}

export function commitAgentImport(commit: AgentImportCommit): Promise<{ profile: { id: string; source: string } }> {
  return rpc('agent/profiles/import/commit', commit)
}

export function discardAgentImport(importId: string): Promise<unknown> {
  return rpc('agent/profiles/import/discard', { importId })
}

export function planAgentExport(id: string, source: string): Promise<AgentExportPlan> {
  return rpc('agent/profiles/export/plan', { id, source })
}

export async function readAgentExport(id: string, source: string, packages: AgentPackageRef[]): Promise<Uint8Array> {
  const parts: Uint8Array[] = []
  let received = 0
  try {
    for (;;) {
      const chunk = await rpc<{ totalBytes: number; dataBase64: string }>('agent/profiles/export/read', { id, source, packages, offset: received })
      const bytes = fromBase64(chunk.dataBase64)
      parts.push(bytes)
      received += bytes.length
      if (received >= chunk.totalBytes || bytes.length === 0) break
    }
  } catch (error) {
    void rpc('agent/profiles/export/read', { id, source, packages, offset: -1 }).catch(() => undefined)
    throw error
  }
  const result = new Uint8Array(received)
  let at = 0
  for (const part of parts) {
    result.set(part, at)
    at += part.length
  }
  return result
}
