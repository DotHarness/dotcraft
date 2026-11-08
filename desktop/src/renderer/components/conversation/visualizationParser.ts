export interface VisualizationContentReference {
  path: string
  title?: string
  mode?: 'wide'
}

const KEYWORD = 'visualize'
const WRAPPED_PREFIX = `${String.fromCharCode(0xe200)}${KEYWORD}${String.fromCharCode(0xe202)}`
const WRAPPED_END = String.fromCharCode(0xe201)
const MAX_TITLE_LENGTH = 200

export function isValidVisualizationPath(path: unknown): path is string {
  return typeof path === 'string' && path.toLowerCase().endsWith('.html') && !/[\\/\0]/.test(path)
}

export function visualizationKey(threadId: string, path: string): string {
  return `${threadId}\n${path}`
}

export function isPendingVisualizationReference(line: string): boolean {
  const tail = line.trimStart()
  return tail !== '' && (
    tail.startsWith(`${KEYWORD}{`) || `${KEYWORD}{`.startsWith(tail) ||
    tail.startsWith(WRAPPED_PREFIX) || WRAPPED_PREFIX.startsWith(tail)
  )
}

export function isCompleteVisualizationReference(line: string): boolean {
  const trimmed = line.trim()
  return (trimmed.startsWith(`${KEYWORD}{`) && trimmed.endsWith('}')) ||
    (trimmed.startsWith(WRAPPED_PREFIX) && trimmed.endsWith(WRAPPED_END))
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
  const { path, title, mode } = value as Record<string, unknown>
  if (!isValidVisualizationPath(path)) return null
  if (title !== undefined && (typeof title !== 'string' || title.trim() === '' || title.length > MAX_TITLE_LENGTH)) return null
  if (mode !== undefined && mode !== 'wide') return null
  return { path, ...(title !== undefined ? { title } : {}), ...(mode !== undefined ? { mode } : {}) }
}
