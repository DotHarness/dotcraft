export type AssistantPart = { kind: 'markdown'; text: string } | { kind: 'interactive'; source: string }

const BLOCK_INFO = 'dotcraft-ui'
const KEYWORD = 'visualize'
const WRAPPED_PREFIX = `\uE200${KEYWORD}\uE202`
const WRAPPED_END = '\uE201'

function isVisualizationReference(line: string): boolean {
  const trimmed = line.trim()
  let json: string
  if (trimmed.startsWith(`${KEYWORD}{`)) json = trimmed.slice(KEYWORD.length)
  else if (trimmed.startsWith(WRAPPED_PREFIX) && trimmed.endsWith(WRAPPED_END)) json = trimmed.slice(WRAPPED_PREFIX.length, -WRAPPED_END.length)
  else return false
  try {
    const value: unknown = JSON.parse(json)
    return !!value && typeof value === 'object' && typeof (value as { path?: unknown }).path === 'string'
  } catch {
    return false
  }
}

function isPendingVisualizationReference(line: string): boolean {
  const tail = line.trimStart()
  return tail !== '' && [`${KEYWORD}{`, WRAPPED_PREFIX].some((prefix) => tail.startsWith(prefix) || prefix.startsWith(tail))
}

interface Fence {
  char: string
  length: number
}

export function splitAssistantText(text: string, streaming: boolean): AssistantPart[] {
  const parts: AssistantPart[] = []
  const lines = text.split('\n')
  let markdown: string[] = []
  let block: { fence: Fence; source: string[] } | null = null
  let fence: Fence | null = null
  const flush = () => {
    const value = markdown.join('\n')
    if (value.trim()) parts.push({ kind: 'markdown', text: value })
    markdown = []
  }
  lines.forEach((rawLine, index) => {
    const last = index === lines.length - 1
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine
    const trimmed = line.trimStart()
    const marker = /^(`{3,}|~{3,})/.exec(trimmed)?.[1]
    const closes = (open: Fence) =>
      marker !== undefined && marker[0] === open.char && marker.length >= open.length && trimmed.slice(marker.length).trim() === ''
    if (block) {
      if (closes(block.fence)) {
        parts.push({ kind: 'interactive', source: block.source.join('\n') })
        block = null
      } else block.source.push(line)
      return
    }
    if (fence) {
      if (closes(fence)) fence = null
    } else if (marker) {
      const info = trimmed.slice(marker.length).trim()
      if (line.length - trimmed.length <= 3 && info.split(/\s/)[0] === BLOCK_INFO) {
        flush()
        block = { fence: { char: marker[0], length: marker.length }, source: [] }
        return
      }
      if (streaming && last && BLOCK_INFO.startsWith(info)) return
      fence = { char: marker[0], length: marker.length }
    } else {
      if (streaming && last && isPendingVisualizationReference(line)) return
      if (isVisualizationReference(line)) return
    }
    markdown.push(rawLine)
  })
  const open = block as { source: string[] } | null
  if (open) parts.push({ kind: 'interactive', source: open.source.join('\n') })
  flush()
  return parts
}

export function copyableAssistantText(text: string): string {
  return splitAssistantText(text, false)
    .flatMap((part) => (part.kind === 'markdown' ? [part.text.trim()] : []))
    .join('\n\n')
}
