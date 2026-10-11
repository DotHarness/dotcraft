export type AssistantPart = { kind: 'markdown'; text: string } | { kind: 'interactive'; source: string }

const BLOCK_OPEN = /^```dotcraft-ui\s*$/
const BLOCK_CLOSE = /^```\s*$/
const FENCE_OPEN = /^ {0,3}(`{3,}|~{3,})/
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

export function splitAssistantText(text: string, streaming: boolean): AssistantPart[] {
  const parts: AssistantPart[] = []
  const lines = text.split('\n')
  let markdown: string[] = []
  let block: string[] | null = null
  let fence: RegExp | null = null
  const flush = () => {
    const value = markdown.join('\n')
    if (value.trim()) parts.push({ kind: 'markdown', text: value })
    markdown = []
  }
  lines.forEach((line, index) => {
    if (block) {
      if (BLOCK_CLOSE.test(line)) {
        parts.push({ kind: 'interactive', source: block.join('\n') })
        block = null
      } else block.push(line)
      return
    }
    if (fence) {
      markdown.push(line)
      if (fence.test(line)) fence = null
      return
    }
    if (BLOCK_OPEN.test(line)) {
      flush()
      block = []
      return
    }
    const open = FENCE_OPEN.exec(line)
    if (open) {
      fence = new RegExp(`^ {0,3}${open[1][0] === '`' ? '`' : '~'}{${open[1].length},}\\s*$`)
      markdown.push(line)
      return
    }
    if (isVisualizationReference(line)) return
    if (streaming && index === lines.length - 1 && isPendingVisualizationReference(line)) return
    markdown.push(line)
  })
  if (block) parts.push({ kind: 'interactive', source: (block as string[]).join('\n') })
  flush()
  return parts
}

export function copyableAssistantText(text: string): string {
  return splitAssistantText(text, false)
    .flatMap((part) => (part.kind === 'markdown' ? [part.text.trim()] : []))
    .join('\n\n')
}
