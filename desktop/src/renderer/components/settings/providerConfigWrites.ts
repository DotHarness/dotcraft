import type { JsonValue } from '@dotcraft/sdk/contracts'
import { configKeyPath } from '../../stores/configStore'
import type { ProviderInfoWire } from './providerInfo'

export interface ProviderFields {
  displayName: string
  protocol: string
  authMethod: 'apiKey' | 'chatgptOAuth'
  apiKey: string | null
  endPoint: string
  networkTimeoutSeconds: number | null
  supportsImageGeneration: boolean | null
}

type ProviderValue = Record<string, JsonValue>

const PROVIDER_WRITE_TIMEOUT_MS = 20_000

export function providerIdExists(providers: readonly ProviderInfoWire[], id: string): boolean {
  const expected = id.toLowerCase()
  return providers.some((provider) => provider.id.toLowerCase() === expected)
}

export function providerCreateValue(fields: ProviderFields): ProviderValue {
  const value: ProviderValue = { Protocol: fields.protocol }
  if (fields.displayName) value.DisplayName = fields.displayName
  if (fields.authMethod === 'chatgptOAuth') value.AuthMethod = fields.authMethod
  else if (fields.apiKey) value.ApiKey = fields.apiKey
  if (fields.endPoint) value.EndPoint = fields.endPoint
  if (fields.networkTimeoutSeconds != null) value.NetworkTimeoutSeconds = fields.networkTimeoutSeconds
  if (fields.supportsImageGeneration != null) value.SupportsImageGeneration = fields.supportsImageGeneration
  return value
}

export function providerUpdateValue(previous: ProviderInfoWire, fields: ProviderFields): ProviderValue {
  const value: ProviderValue = {}
  const change = (key: string, before: JsonValue, after: JsonValue): void => {
    if (before !== after) value[key] = after === '' ? null : after
  }
  if ((fields.displayName || previous.id) !== previous.displayName) value.DisplayName = fields.displayName || null
  change('Protocol', previous.protocol, fields.protocol)
  change('AuthMethod', previous.authMethod ?? 'apiKey', fields.authMethod)
  change('EndPoint', previous.endPoint, fields.endPoint)
  change('NetworkTimeoutSeconds', previous.networkTimeoutSeconds ?? null, fields.networkTimeoutSeconds)
  const previousImageGeneration = previous.supportsImageGeneration === true
  if (fields.supportsImageGeneration == null) {
    if (previousImageGeneration) value.SupportsImageGeneration = null
  } else if (
    fields.supportsImageGeneration !== previousImageGeneration ||
    'Protocol' in value || 'AuthMethod' in value || 'EndPoint' in value
  ) {
    value.SupportsImageGeneration = fields.supportsImageGeneration
  }
  if (fields.authMethod === 'chatgptOAuth') {
    if (previous.hasApiKey) value.ApiKey = null
  } else if (fields.apiKey !== null) {
    value.ApiKey = fields.apiKey || null
  }
  return value
}

export class ProviderExistsError extends Error {
  constructor(readonly providerId: string) {
    super(`Provider '${providerId}' already exists.`)
  }
}

async function readVersionWithoutProvider(id: string): Promise<string> {
  const result = await window.api.appServer.sendRequest('config/read', { includeLayers: true }, PROVIDER_WRITE_TIMEOUT_MS) as {
    layers?: Array<{ name?: { type?: string }; version?: string; config?: Record<string, JsonValue> }>
  } | null
  const layer = result?.layers?.find((entry) => entry.name?.type === 'user')
  if (!layer?.version || !layer.config) throw new Error('The user configuration could not be read.')
  const providers = Object.entries(layer.config).find(([key]) => key.toLowerCase() === 'providers')?.[1]
  const expected = id.toLowerCase()
  if (
    providers && typeof providers === 'object' && !Array.isArray(providers) &&
    Object.keys(providers).some((key) => key.toLowerCase() === expected)
  ) {
    throw new ProviderExistsError(id)
  }
  return layer.version
}

async function writeProviderEntry(
  id: string,
  value: ProviderValue | null,
  mergeStrategy: 'replace' | 'upsert',
  expectedVersion?: string
): Promise<void> {
  await window.api.appServer.sendRequest('config/batchWrite', {
    edits: [{ keyPath: configKeyPath('Providers', id), value, mergeStrategy }],
    ...(expectedVersion ? { expectedVersion } : {})
  }, PROVIDER_WRITE_TIMEOUT_MS)
}

export async function createProvider(id: string, fields: ProviderFields): Promise<void> {
  await writeProviderEntry(id, providerCreateValue(fields), 'replace', await readVersionWithoutProvider(id))
}

export async function updateProvider(previous: ProviderInfoWire, fields: ProviderFields): Promise<void> {
  const value = providerUpdateValue(previous, fields)
  if (Object.keys(value).length > 0) await writeProviderEntry(previous.id, value, 'upsert')
}

export async function deleteProvider(id: string): Promise<void> {
  await writeProviderEntry(id, null, 'replace')
}
