import yaml from 'js-yaml'

export interface MemoryFileFields {
  name: string
  description: string
  type: string
  body: string
}

type YamlMap = Record<string, unknown>

export interface ParsedMemoryFile extends MemoryFileFields {
  hasFrontmatter: boolean
  invalidHeader: boolean
  extra: { top: YamlMap; metadata: YamlMap }
}

function isMap(value: unknown): value is YamlMap {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' ? String(value) : ''
}

export function parseMemoryFile(source: string): ParsedMemoryFile {
  const normalized = source.replace(/^﻿/, '').replace(/\r\n/g, '\n')
  const lines = normalized.split('\n')
  const empty: ParsedMemoryFile = {
    name: '', description: '', type: '', body: normalized, hasFrontmatter: false, invalidHeader: false, extra: { top: {}, metadata: {} }
  }
  if (lines[0]?.trim() !== '---') return empty
  const end = lines.findIndex((line, index) => index > 0 && line.trim() === '---')
  if (end < 0) return empty

  const body = lines.slice(end + 1).join('\n').replace(/^\n+/, '')
  let header: unknown
  try {
    header = yaml.load(lines.slice(1, end).join('\n'), { schema: yaml.CORE_SCHEMA }) ?? {}
  } catch {
    header = null
  }
  if (!isMap(header)) return { ...empty, hasFrontmatter: true, invalidHeader: true, body }

  const { name, description, metadata, ...top } = header
  const { type, ...metadataRest } = isMap(metadata) ? metadata : {}
  return {
    name: text(name),
    description: text(description),
    type: text(type),
    body,
    hasFrontmatter: true,
    invalidHeader: false,
    extra: { top, metadata: metadataRest }
  }
}

export function serializeMemoryFile(fields: MemoryFileFields, extra: ParsedMemoryFile['extra'] = { top: {}, metadata: {} }): string {
  const header = yaml.dump(
    {
      name: fields.name.trim(),
      description: fields.description.trim(),
      ...extra.top,
      metadata: { type: fields.type, ...extra.metadata }
    },
    { schema: yaml.CORE_SCHEMA, lineWidth: -1 }
  )
  const body = fields.body.replace(/\r\n/g, '\n').replace(/^\n+/, '').replace(/\s+$/, '')
  return `---\n${header}---\n\n${body}\n`
}

/** File names are the store's identity for a memory; Core reads any top-level Markdown file except the index. */
export function isValidMemoryName(name: string): boolean {
  return /^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$/.test(name) && !/\.md$/i.test(name) && name.toLowerCase() !== 'memory'
}

export function memoryFileName(name: string): string {
  return `${name}.md`
}

export function memoryName(fileName: string): string {
  return fileName.replace(/\.md$/i, '')
}
