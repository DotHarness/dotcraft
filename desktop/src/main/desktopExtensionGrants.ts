import { randomUUID } from 'crypto'
import { promises as fs } from 'fs'
import * as path from 'path'

export interface DesktopExtensionGrant {
  grantId: string
  pluginId: string
  extensionId: string
  rootPath: string
  connectOrigins: string[]
  surfaceWriteScopes: string[]
  requiredAppIds: string[]
  appProtocols: Record<string, string[]>
}

interface DesktopExtensionDescriptor {
  id?: unknown
  connectOrigins?: unknown
  surfaceWriteScopes?: unknown
  requiredAppIds?: unknown
}

const grants = new Map<string, DesktopExtensionGrant>()

export async function authorizeDesktopExtensionGrant(params: {
  pluginId: string
  rootPath: string
  extensionId: string
}): Promise<{ grantId: string }> {
  const pluginId = normalizePluginId(params.pluginId)
  const extensionId = typeof params.extensionId === 'string' ? params.extensionId.trim() : ''
  if (!pluginId) throw new Error('Plugin id is required')
  if (!extensionId) throw new Error('Desktop extension id is required')
  if (!path.isAbsolute(params.rootPath)) {
    throw new Error('Plugin root must be an absolute path')
  }

  const rootPath = await fs.realpath(path.resolve(params.rootPath))
  const manifest = await readJsonObject(path.join(rootPath, '.craft-plugin', 'plugin.json'))
  const manifestPluginId = normalizePluginId(stringField(manifest, 'id'))
  if (manifestPluginId !== pluginId) {
    throw new Error(`Plugin root id mismatch for ${params.pluginId}`)
  }

  const desktopExtensionsPath = resolveManifestRelativePath(
    rootPath,
    stringField(manifest, 'desktopExtensions'),
    'desktopExtensions'
  )
  const extensionDocument = await readJsonObject(desktopExtensionsPath)
  const descriptors = arrayField(extensionDocument, 'extensions')
  const descriptor = descriptors.find((candidate): candidate is Record<string, unknown> =>
    isRecord(candidate) && stringField(candidate, 'id') === extensionId)
  if (!descriptor) {
    throw new Error(`Desktop extension '${params.extensionId}' is not declared by plugin '${params.pluginId}'.`)
  }

  const grantId = randomUUID()
  grants.set(grantId, {
    grantId,
    pluginId,
    extensionId,
    rootPath,
    connectOrigins: stringArrayField(descriptor, 'connectOrigins'),
    surfaceWriteScopes: stringArrayField(descriptor, 'surfaceWriteScopes'),
    requiredAppIds: stringArrayField(descriptor, 'requiredAppIds'),
    appProtocols: await readPluginAppProtocols(rootPath, stringField(manifest, 'apps'))
  })
  return { grantId }
}

export function getDesktopExtensionGrant(grantId: unknown): DesktopExtensionGrant | null {
  if (typeof grantId !== 'string' || grantId.trim() === '') return null
  return grants.get(grantId) ?? null
}

export function requireDesktopExtensionGrant(grantId: unknown): DesktopExtensionGrant {
  const grant = getDesktopExtensionGrant(grantId)
  if (!grant) {
    throw new Error('Desktop extension grant is not valid.')
  }
  return grant
}

export function revokeDesktopExtensionGrant(grantId: string): void {
  grants.delete(grantId)
}

export function clearDesktopExtensionGrants(): void {
  grants.clear()
}

export function ensureDesktopExtensionAppAllowed(
  grant: DesktopExtensionGrant,
  appId: string
): void {
  if (!grant.requiredAppIds.some((candidate) => candidate === appId)) {
    throw new Error(`Desktop extension '${grant.extensionId}' is not allowed to access app '${appId}'.`)
  }
}

export function ensureDesktopExtensionAppUrlAllowed(
  grant: DesktopExtensionGrant,
  appId: string,
  url: string
): void {
  ensureDesktopExtensionAppAllowed(grant, appId)
  const allowedProtocols = grant.appProtocols[appId] ?? []
  if (allowedProtocols.length === 0) {
    throw new Error(`Desktop extension '${grant.extensionId}' cannot open app '${appId}' because no native protocol is declared.`)
  }

  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    throw new Error('Invalid app URL')
  }

  const protocol = normalizeProtocol(parsed.protocol)
  if (!allowedProtocols.includes(protocol)) {
    throw new Error(`Desktop extension '${grant.extensionId}' is not allowed to open this app URL.`)
  }
}

async function readPluginAppProtocols(
  rootPath: string,
  appsPathValue: string | null
): Promise<Record<string, string[]>> {
  if (!appsPathValue) return {}
  const appsPath = resolveManifestRelativePath(rootPath, appsPathValue, 'apps')
  const document = await readJsonObject(appsPath)
  const result: Record<string, string[]> = {}
  for (const rawApp of arrayField(document, 'apps')) {
    if (!isRecord(rawApp)) continue
    const appId = stringField(rawApp, 'appId')
    if (!appId) continue
    const protocols = new Set<string>()
    const nativeApplication = objectField(rawApp, 'nativeApplication')
    addProtocol(protocols, stringField(nativeApplication, 'protocol'))
    const platforms = objectField(nativeApplication, 'platforms')
    for (const platform of Object.values(platforms)) {
      if (isRecord(platform)) {
        addProtocol(protocols, stringField(platform, 'protocol'))
      }
    }
    result[appId] = [...protocols]
  }
  return result
}

function resolveManifestRelativePath(rootPath: string, value: string | null, fieldName: string): string {
  if (!value || !value.startsWith('./')) {
    throw new Error(`Plugin manifest field '${fieldName}' must be a manifest-relative path.`)
  }
  const relative = value.slice(2)
  if (relative.split(/[\\/]/).some((segment) => segment === '..')) {
    throw new Error(`Plugin manifest field '${fieldName}' must stay inside the plugin root.`)
  }
  const resolved = path.resolve(rootPath, relative)
  if (!isPathWithin(resolved, rootPath)) {
    throw new Error(`Plugin manifest field '${fieldName}' must stay inside the plugin root.`)
  }
  return resolved
}

async function readJsonObject(filePath: string): Promise<Record<string, unknown>> {
  const stat = await fs.stat(filePath)
  if (!stat.isFile()) {
    throw new Error(`Expected a file at ${filePath}`)
  }
  const parsed = JSON.parse(await fs.readFile(filePath, 'utf8')) as unknown
  if (!isRecord(parsed)) {
    throw new Error(`Expected JSON object at ${filePath}`)
  }
  return parsed
}

function isPathWithin(targetPath: string, rootPath: string): boolean {
  const rel = path.relative(rootPath, targetPath)
  return rel === '' || (!!rel && !rel.startsWith('..') && !path.isAbsolute(rel))
}

function stringField(record: Record<string, unknown> | null, field: string): string | null {
  if (!record) return null
  const value = record[field]
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : null
}

function stringArrayField(record: Record<string, unknown>, field: keyof DesktopExtensionDescriptor): string[] {
  const value = record[field]
  return Array.isArray(value)
    ? value.filter((entry): entry is string => typeof entry === 'string' && entry.trim() !== '').map((entry) => entry.trim())
    : []
}

function arrayField(record: Record<string, unknown>, field: string): unknown[] {
  const value = record[field]
  return Array.isArray(value) ? value : []
}

function objectField(record: Record<string, unknown> | null, field: string): Record<string, unknown> {
  const value = record?.[field]
  return isRecord(value) ? value : {}
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function normalizePluginId(value: string | null | undefined): string {
  return typeof value === 'string' ? value.trim().toLowerCase() : ''
}

function addProtocol(protocols: Set<string>, value: string | null): void {
  const normalized = normalizeProtocol(value)
  if (normalized) protocols.add(normalized)
}

function normalizeProtocol(value: string | null | undefined): string {
  if (typeof value !== 'string') return ''
  return value.trim().replace(/:$/, '').toLowerCase()
}
