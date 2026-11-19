export interface MemoryFileFields {
  name: string
  description: string
  type: string
  body: string
}

export interface ParsedMemoryFile extends MemoryFileFields {
  hasFrontmatter: boolean
  /** Frontmatter lines this editor does not own, kept verbatim on save. */
  extraFrontmatter: string[]
}

const OWNED_KEYS = new Set(['name', 'description', 'metadata'])

function unquote(value: string): string {
  const trimmed = value.trim()
  if (trimmed.length >= 2 && trimmed.startsWith('"') && trimmed.endsWith('"')) {
    try {
      return JSON.parse(trimmed) as string
    } catch {
      return trimmed.slice(1, -1)
    }
  }
  if (trimmed.length >= 2 && trimmed.startsWith("'") && trimmed.endsWith("'")) return trimmed.slice(1, -1).replace(/''/g, "'")
  return trimmed
}

/** A plain scalar unless YAML would read it differently, then a double-quoted one. */
function scalar(value: string): string {
  const plain = value.trim()
  if (plain === '') return '""'
  return /^[\s"'#&*!|>%@`[\]{},?:-]|: | #|\s$/.test(plain) || /^(?:true|false|null|yes|no|on|off|~)$/i.test(plain) || /^[-+.]?\d/.test(plain)
    ? JSON.stringify(plain)
    : plain
}

export function parseMemoryFile(text: string): ParsedMemoryFile {
  const normalized = text.replace(/^\uFEFF/, '').replace(/\r\n/g, '\n')
  const lines = normalized.split('\n')
  const empty: ParsedMemoryFile = { name: '', description: '', type: '', body: normalized, hasFrontmatter: false, extraFrontmatter: [] }
  if (lines[0]?.trim() !== '---') return empty
  const end = lines.findIndex((line, index) => index > 0 && line.trim() === '---')
  if (end < 0) return empty

  const result: ParsedMemoryFile = { ...empty, hasFrontmatter: true, body: lines.slice(end + 1).join('\n').replace(/^\n+/, '') }
  let inMetadata = false
  for (const line of lines.slice(1, end)) {
    const nested = /^\s+([A-Za-z_][\w-]*)\s*:(.*)$/.exec(line)
    if (inMetadata && nested) {
      if (nested[1] === 'type') result.type = unquote(nested[2])
      else result.extraFrontmatter.push(`metadata.${line}`)
      continue
    }
    const top = /^([A-Za-z_][\w-]*)\s*:(.*)$/.exec(line)
    inMetadata = top?.[1] === 'metadata'
    if (top && top[1] === 'name') result.name = unquote(top[2])
    else if (top && top[1] === 'description') result.description = unquote(top[2])
    else if (!top || !OWNED_KEYS.has(top[1])) result.extraFrontmatter.push(line)
  }
  return result
}

export function serializeMemoryFile(fields: MemoryFileFields, extraFrontmatter: readonly string[] = []): string {
  const metadataExtras = extraFrontmatter.filter((line) => line.startsWith('metadata.')).map((line) => line.slice('metadata.'.length))
  const topExtras = extraFrontmatter.filter((line) => !line.startsWith('metadata.'))
  const header = [
    '---',
    `name: ${scalar(fields.name)}`,
    `description: ${scalar(fields.description)}`,
    ...topExtras,
    'metadata:',
    `  type: ${scalar(fields.type)}`,
    ...metadataExtras,
    '---'
  ]
  const body = fields.body.replace(/\r\n/g, '\n').replace(/^\n+/, '').replace(/\s+$/, '')
  return `${header.join('\n')}\n\n${body}\n`
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
