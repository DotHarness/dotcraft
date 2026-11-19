import type { ConfigEdit, ConfigReadResult, ConfigSchemaResult, JsonValue } from '@dotcraft/sdk/contracts'

export interface MemoryType {
  name: string
  description: string
}

export interface PolicyValues {
  types: MemoryType[]
  saveGuidance: string
  extractionGuidance: string
  consolidationGuidance: string
  extractionInterval: number
}

export type PolicySetting = keyof PolicyValues
export type PolicyOrigin = 'default' | 'user' | 'workspace'
export type PolicyLayerKind = 'user' | 'workspace'

export const POLICY_SETTINGS: readonly PolicySetting[] = [
  'types', 'saveGuidance', 'extractionGuidance', 'consolidationGuidance', 'extractionInterval'
]

const FIELD_KEYS: Record<PolicySetting, string> = {
  types: 'Types',
  saveGuidance: 'SaveGuidance',
  extractionGuidance: 'ExtractionGuidance',
  consolidationGuidance: 'ConsolidationGuidance',
  extractionInterval: 'ExtractionInterval'
}

export function policyKeyPath(setting: PolicySetting): string {
  return `Memory.${FIELD_KEYS[setting]}`
}

export const POLICY_LIMITS = {
  minTypes: 1,
  maxTypes: 8,
  maxTypeName: 40,
  maxTypeDescription: 200,
  maxGuidance: 8000,
  minInterval: 1,
  maxInterval: 50
} as const

export interface PolicyLayer {
  kind: PolicyLayerKind
  file: string
  version: string
  values: Partial<PolicyValues>
}

export interface PolicySwitches {
  memoryEnabled: boolean
  autoExtract: boolean
  dreamsEnabled: boolean
}

export interface PolicyState {
  defaults: PolicyValues
  effective: PolicyValues
  origins: Record<PolicySetting, PolicyOrigin>
  layers: Partial<Record<PolicyLayerKind, PolicyLayer>>
  switches: PolicySwitches
}

type JsonRecord = Record<string, unknown>

function isRecord(value: unknown): value is JsonRecord {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

/** Configuration files may spell keys in any case. */
function child(record: unknown, key: string): unknown {
  if (!isRecord(record)) return undefined
  if (Object.hasOwn(record, key)) return record[key]
  const match = Object.keys(record).find((candidate) => candidate.toLowerCase() === key.toLowerCase())
  return match === undefined ? undefined : record[match]
}

function readTypes(value: unknown): MemoryType[] | undefined {
  if (!Array.isArray(value)) return undefined
  return value.filter(isRecord).map((entry) => ({
    name: String(child(entry, 'Name') ?? ''),
    description: String(child(entry, 'Description') ?? '')
  }))
}

function readSetting(setting: PolicySetting, value: unknown): PolicyValues[PolicySetting] | undefined {
  if (value === undefined || value === null) return undefined
  if (setting === 'types') return readTypes(value)
  if (setting === 'extractionInterval') return typeof value === 'number' ? value : undefined
  return typeof value === 'string' ? value : undefined
}

function readValues(memory: unknown): Partial<PolicyValues> {
  const values: Partial<Record<PolicySetting, unknown>> = {}
  for (const setting of POLICY_SETTINGS) {
    const value = readSetting(setting, child(memory, FIELD_KEYS[setting]))
    if (value !== undefined) values[setting] = value
  }
  return values as Partial<PolicyValues>
}

export function readPolicyDefaults(schema: ConfigSchemaResult): PolicyValues {
  const section = schema.sections?.find((candidate) => candidate.path?.length === 1 && candidate.path[0].toLowerCase() === 'memory')
  const defaults: Partial<Record<PolicySetting, unknown>> = {}
  for (const setting of POLICY_SETTINGS) {
    const field = section?.fields.find((candidate) => candidate.key.toLowerCase() === FIELD_KEYS[setting].toLowerCase())
    defaults[setting] = readSetting(setting, field?.defaultValue)
  }
  return defaults as unknown as PolicyValues
}

export function readPolicyState(defaults: PolicyValues, read: ConfigReadResult): PolicyState {
  const layers: Partial<Record<PolicyLayerKind, PolicyLayer>> = {}
  for (const layer of read.layers ?? []) {
    const kind = layer.name.type === 'workspace' ? 'workspace' : layer.name.type === 'user' ? 'user' : null
    if (!kind) continue
    layers[kind] = { kind, file: layer.name.file, version: layer.version, values: readValues(child(layer.config, 'Memory')) }
  }
  const origins = {} as Record<PolicySetting, PolicyOrigin>
  const effective = { ...defaults } as Record<PolicySetting, unknown>
  for (const setting of POLICY_SETTINGS) {
    const workspace = layers.workspace?.values[setting]
    const user = layers.user?.values[setting]
    origins[setting] = workspace !== undefined ? 'workspace' : user !== undefined ? 'user' : 'default'
    effective[setting] = workspace ?? user ?? defaults[setting]
  }
  const memory = child(read.config, 'Memory')
  return {
    defaults,
    effective: effective as unknown as PolicyValues,
    origins,
    layers,
    switches: {
      memoryEnabled: child(memory, 'Enabled') !== false,
      autoExtract: child(memory, 'AutoExtract') === true,
      dreamsEnabled: child(child(read.config, 'Dreams'), 'Enabled') === true
    }
  }
}

function normalizeText(value: string): string {
  return value.replace(/\r\n/g, '\n').trim()
}

export function settingEquals<S extends PolicySetting>(setting: S, a: PolicyValues[S], b: PolicyValues[S]): boolean {
  if (setting === 'types') {
    const left = a as MemoryType[]
    const right = b as MemoryType[]
    return left.length === right.length && left.every((type, index) =>
      type.name.trim() === right[index].name.trim() && type.description.trim() === right[index].description.trim())
  }
  if (setting === 'extractionInterval') return a === b
  return normalizeText(a as string) === normalizeText(b as string)
}

export function policyEquals(a: PolicyValues, b: PolicyValues): boolean {
  return POLICY_SETTINGS.every((setting) => settingEquals(setting, a[setting], b[setting]))
}

function wireValue(setting: PolicySetting, value: PolicyValues[PolicySetting]): JsonValue {
  if (setting === 'types') {
    return (value as MemoryType[]).map((type) => ({ Name: type.name.trim(), Description: type.description.trim() }))
  }
  return typeof value === 'string' ? value.replace(/\r\n/g, '\n').trim() : value as number
}

export function settingEdit(setting: PolicySetting, value: PolicyValues[PolicySetting] | null): ConfigEdit {
  return { keyPath: policyKeyPath(setting), value: value === null ? null : wireValue(setting, value), mergeStrategy: 'replace' }
}

export function draftEdits(draft: PolicyValues, effective: PolicyValues): ConfigEdit[] {
  return POLICY_SETTINGS
    .filter((setting) => !settingEquals(setting, draft[setting], effective[setting]))
    .map((setting) => settingEdit(setting, draft[setting]))
}

export type PolicyIssue =
  | 'typesCount' | 'typeName' | 'typeDuplicate' | 'typeDescription'
  | 'guidanceEmpty' | 'guidanceTooLong' | 'interval'

export function validateSetting<S extends PolicySetting>(setting: S, value: PolicyValues[S]): PolicyIssue | null {
  if (setting === 'types') {
    const types = value as MemoryType[]
    if (types.length < POLICY_LIMITS.minTypes || types.length > POLICY_LIMITS.maxTypes) return 'typesCount'
    const names = new Set<string>()
    for (const type of types) {
      const name = type.name.trim()
      if (name.length > POLICY_LIMITS.maxTypeName || !/^[a-z0-9]+(-[a-z0-9]+)*$/.test(name)) return 'typeName'
      if (names.has(name)) return 'typeDuplicate'
      names.add(name)
      const description = type.description.trim()
      if (!description || description.length > POLICY_LIMITS.maxTypeDescription || /[\r\n]/.test(description)) return 'typeDescription'
    }
    return null
  }
  if (setting === 'extractionInterval') {
    const interval = value as number
    return Number.isInteger(interval) && interval >= POLICY_LIMITS.minInterval && interval <= POLICY_LIMITS.maxInterval ? null : 'interval'
  }
  const text = normalizeText(value as string)
  if (!text) return 'guidanceEmpty'
  return text.length > POLICY_LIMITS.maxGuidance ? 'guidanceTooLong' : null
}

export function validatePolicy(values: PolicyValues): Partial<Record<PolicySetting, PolicyIssue>> {
  const issues: Partial<Record<PolicySetting, PolicyIssue>> = {}
  for (const setting of POLICY_SETTINGS) {
    const issue = validateSetting(setting, values[setting])
    if (issue) issues[setting] = issue
  }
  return issues
}
