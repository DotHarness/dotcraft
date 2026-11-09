export const MCP_SETTINGS_CAPABILITY = 'dotcraft/settings'

export type McpSettingsValue = boolean | string | number

export interface McpSettingsProperty {
  type: 'boolean' | 'string' | 'number' | 'integer'
  title?: string
  description?: string
  enum?: string[]
  minimum?: number
  maximum?: number
  multipleOf?: number
  minLength?: number
  maxLength?: number
  pattern?: string
  writeOnly?: boolean
}

export type McpSettingsLayoutItem =
  | { kind: 'property'; property: string }
  | { kind: 'tool'; tool: string; title?: string; description?: string }

export interface McpSettingsLayoutGroup {
  title: string
  items: McpSettingsLayoutItem[]
}

export interface McpSettingsDefinition {
  properties: Record<string, McpSettingsProperty>
  values: Record<string, McpSettingsValue>
  layout?: McpSettingsLayoutGroup[]
}

export interface McpSettingsGroup {
  title?: string
  other: boolean
  items: McpSettingsLayoutItem[]
}

export interface McpSettingsTools {
  readTool: string
  updateTool: string
}

export interface McpSettingsServerStatus {
  name: string
  declaredName?: string | null
  startupState: string
  serverCapabilities?: unknown
  tools?: Record<string, { name: string; inputSchema?: unknown }> | null
  origin?: { kind?: string; pluginId?: string | null } | null
}

const PROPERTY_TYPES = new Set(['boolean', 'string', 'number', 'integer'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function nonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function readSettingsTools(capabilities: unknown): McpSettingsTools | null {
  if (!isRecord(capabilities)) return null
  const extensions = isRecord(capabilities.extensions) ? capabilities.extensions : {}
  const experimental = isRecord(capabilities.experimental) ? capabilities.experimental : {}
  const declaration = extensions[MCP_SETTINGS_CAPABILITY] ?? experimental[MCP_SETTINGS_CAPABILITY]
  if (!isRecord(declaration)) return null
  const { readTool, updateTool } = declaration
  if (!nonEmptyString(readTool) || !nonEmptyString(updateTool) || readTool === updateTool) return null
  return { readTool, updateTool }
}

export function hasTool(status: McpSettingsServerStatus, name: string): boolean {
  return status.tools != null && Object.hasOwn(status.tools, name)
}

export function settingsToolsForServer(status: McpSettingsServerStatus): McpSettingsTools | null {
  const tools = readSettingsTools(status.serverCapabilities)
  return tools && hasTool(status, tools.readTool) ? tools : null
}

export function toolRequiresArguments(status: McpSettingsServerStatus, name: string): boolean {
  const schema = status.tools?.[name]?.inputSchema
  return isRecord(schema) && Array.isArray(schema.required) && schema.required.length > 0
}

function isProperty(value: unknown): value is McpSettingsProperty {
  if (!isRecord(value) || typeof value.type !== 'string' || !PROPERTY_TYPES.has(value.type)) return false
  return value.enum === undefined
    || (value.type === 'string' && Array.isArray(value.enum) && value.enum.every((option) => typeof option === 'string'))
}

function isLayout(value: unknown, properties: Record<string, McpSettingsProperty>): value is McpSettingsLayoutGroup[] {
  return Array.isArray(value) && value.every((group) =>
    isRecord(group) && nonEmptyString(group.title) && Array.isArray(group.items) && group.items.every((item) =>
      isRecord(item) && (
        (item.kind === 'property' && typeof item.property === 'string' && Object.hasOwn(properties, item.property))
        || (item.kind === 'tool' && nonEmptyString(item.tool)))))
}

/** Rejects the whole definition when any value is invalid, as the server is the authority. */
export function parseSettingsDefinition(structuredContent: unknown): McpSettingsDefinition | null {
  if (!isRecord(structuredContent)) return null
  const { schema, values, layout } = structuredContent
  if (!isRecord(schema) || schema.type !== 'object' || !isRecord(schema.properties) || !isRecord(values)) return null
  const properties: Record<string, McpSettingsProperty> = {}
  const parsedValues: Record<string, McpSettingsValue> = {}
  for (const [key, property] of Object.entries(schema.properties)) {
    const value = values[key]
    if (!isProperty(property) || !isValidValue(property, value)) return null
    properties[key] = property
    parsedValues[key] = value
  }
  if (layout !== undefined && !isLayout(layout, properties)) return null
  return { properties, values: parsedValues, layout }
}

function isMultiple(value: number, multipleOf: number | undefined): boolean {
  if (!multipleOf) return true
  const quotient = value / multipleOf
  return Math.abs(quotient - Math.round(quotient)) < 1e-9
}

function isValidText(property: McpSettingsProperty, value: string): boolean {
  const length = Array.from(value).length
  let matches = true
  if (property.pattern !== undefined) {
    try {
      matches = new RegExp(property.pattern, 'u').test(value)
    } catch {
      matches = false
    }
  }
  return matches
    && (property.enum === undefined || property.enum.includes(value))
    && (property.minLength === undefined || length >= property.minLength)
    && (property.maxLength === undefined || length <= property.maxLength)
}

function isValidNumber(property: McpSettingsProperty, value: number): boolean {
  return Number.isFinite(value)
    && (property.type !== 'integer' || Number.isInteger(value))
    && (property.minimum === undefined || value >= property.minimum)
    && (property.maximum === undefined || value <= property.maximum)
    && isMultiple(value, property.multipleOf)
}

function isValidValue(property: McpSettingsProperty, value: unknown): value is McpSettingsValue {
  switch (property.type) {
    case 'boolean':
      return typeof value === 'boolean'
    case 'string':
      // A credential reads back empty, so its constraints apply only to what the user enters.
      return typeof value === 'string' && ((property.writeOnly && value === '') || isValidText(property, value))
    default:
      return typeof value === 'number' && isValidNumber(property, value)
  }
}

export type McpSettingsInputError = 'integer' | 'range' | 'constraint'

export function parseSettingsInput(
  property: McpSettingsProperty,
  raw: string
): { value: McpSettingsValue } | { error: McpSettingsInputError } {
  if (property.type === 'number' || property.type === 'integer') {
    const value = raw.trim() === '' ? Number.NaN : Number(raw)
    if (!Number.isFinite(value)) return { error: 'constraint' }
    if (property.type === 'integer' && !Number.isInteger(value)) return { error: 'integer' }
    if ((property.minimum !== undefined && value < property.minimum)
      || (property.maximum !== undefined && value > property.maximum)) return { error: 'range' }
    return isMultiple(value, property.multipleOf) ? { value } : { error: 'constraint' }
  }
  return isValidText(property, raw) ? { value: raw } : { error: 'constraint' }
}

export function settingsGroups(definition: McpSettingsDefinition): McpSettingsGroup[] {
  const groups: McpSettingsGroup[] = (definition.layout ?? []).map((group) => ({ ...group, other: false }))
  const placed = new Set(groups.flatMap((group) =>
    group.items.flatMap((item) => (item.kind === 'property' ? [item.property] : []))))
  const rest = Object.keys(definition.properties).filter((key) => !placed.has(key))
  if (rest.length > 0) {
    groups.push({
      other: definition.layout !== undefined,
      items: rest.map((property) => ({ kind: 'property', property }))
    })
  }
  return groups.filter((group) => group.items.length > 0)
}

export function firstTextContent(content: readonly unknown[] | undefined): string | null {
  for (const item of content ?? []) {
    if (isRecord(item) && item.type === 'text' && nonEmptyString(item.text)) return item.text
  }
  return null
}
