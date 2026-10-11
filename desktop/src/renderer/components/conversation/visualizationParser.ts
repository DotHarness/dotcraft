export interface VisualizationContentReference {
  path: string
  title?: string
  mode?: 'wide'
}

const KEYWORD = 'visualize'
const WRAPPED_PREFIX = `${String.fromCharCode(0xe200)}${KEYWORD}${String.fromCharCode(0xe202)}`
const WRAPPED_END = String.fromCharCode(0xe201)
const MAX_TITLE_LENGTH = 200
const ABSOLUTE_PATH = /^(?:[A-Za-z]:[\\/]|\\\\|\/)/
const FILE_NAME = /^[a-z0-9]+(?:-[a-z0-9]+)*\.html$/

export function isValidVisualizationPath(path: unknown): path is string {
  if (typeof path !== 'string' || path.trim() === '' || !ABSOLUTE_PATH.test(path)) return false
  const segments = path.split(/[\\/]/)
  return !segments.some(segment => segment === '.' || segment === '..') && FILE_NAME.test(segments[segments.length - 1])
}

export function normalizeVisualizationPath(path: string): string {
  const normalized = path.replace(/\\/g, '/')
  return /^[A-Za-z]:\//.test(normalized) || normalized.startsWith('//') ? normalized.toLowerCase() : normalized
}

export function visualizationKey(threadId: string, path: string): string {
  return `${threadId}\n${normalizeVisualizationPath(path)}`
}

export function visualizationFileName(path: string): string {
  const segments = path.split(/[\\/]/)
  return segments[segments.length - 1]
}

export function isPendingVisualizationReference(line: string): boolean {
  const tail = line.trimStart()
  return tail !== '' && (
    tail.startsWith(`${KEYWORD}{`) || `${KEYWORD}{`.startsWith(tail) ||
    tail.startsWith(WRAPPED_PREFIX) || WRAPPED_PREFIX.startsWith(tail)
  )
}

export function parseVisualizationReferenceLine(line: string): VisualizationContentReference | null {
  const trimmed = line.trim()
  let json: string
  if (trimmed.startsWith(`${KEYWORD}{`)) json = trimmed.slice(KEYWORD.length)
  else if (trimmed.length > WRAPPED_PREFIX.length + 1 && trimmed.startsWith(WRAPPED_PREFIX) && trimmed.endsWith(WRAPPED_END)) {
    json = trimmed.slice(WRAPPED_PREFIX.length, -WRAPPED_END.length)
  } else return null

  let value: unknown
  try {
    value = JSON.parse(json)
  } catch {
    return null
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null
  const fields = value as Record<string, unknown>
  if (Object.keys(fields).some(key => key !== 'path' && key !== 'title' && key !== 'mode')) return null
  if (Object.values(fields).some(field => typeof field !== 'string')) return null
  const { path, title, mode } = fields as { path?: string; title?: string; mode?: string }
  if (!isValidVisualizationPath(path)) return null
  if (title !== undefined && (title.trim() === '' || title.length > MAX_TITLE_LENGTH)) return null
  if (mode !== undefined && mode !== 'wide') return null
  return { path, ...(title !== undefined ? { title } : {}), ...(mode !== undefined ? { mode } : {}) }
}
