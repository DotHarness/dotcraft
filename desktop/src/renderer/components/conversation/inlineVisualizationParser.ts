export interface InlineVisualizationDirective {
  file: string
  start: number
  end: number
}

const DIRECTIVE = /^\s*::dotcraft-inline-vis\{file="([a-z0-9]+(?:-[a-z0-9]+)*\.html)"\}\s*$/

export function parseInlineVisualizations(markdown: string): InlineVisualizationDirective[] {
  const result: InlineVisualizationDirective[] = []
  let inFence = false
  let fenceChar = ''
  let fenceLength = 0
  let offset = 0
  for (const rawLine of markdown.split('\n')) {
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine
    const trimmed = line.trimStart()
    const fence = /^(?<fence>`{3,}|~{3,})/.exec(trimmed)?.groups?.fence
    if (fence) {
      if (!inFence) {
        inFence = true
        fenceChar = fence[0]
        fenceLength = fence.length
      } else if (fence[0] === fenceChar && fence.length >= fenceLength && trimmed.slice(fence.length).trim() === '') {
        inFence = false
      }
    } else if (!inFence) {
      const match = DIRECTIVE.exec(line)
      if (match) result.push({ file: match[1], start: offset, end: offset + rawLine.length })
    }
    offset += rawLine.length + 1
  }
  return result
}

export function stripInlineVisualizationDirectives(markdown: string): string {
  const directives = parseInlineVisualizations(markdown)
  if (directives.length === 0) return markdown
  let output = ''
  let cursor = 0
  for (const directive of directives) {
    output += markdown.slice(cursor, directive.start)
    cursor = directive.end
    if (markdown[cursor] === '\n') cursor++
  }
  return output + markdown.slice(cursor)
}

export function hideStreamingVisualizationTail(markdown: string): string {
  const lastNewline = markdown.lastIndexOf('\n')
  const tail = markdown.slice(lastNewline + 1).trimStart()
  return tail.startsWith('::dotcraft-inline-vis')
    ? markdown.slice(0, lastNewline < 0 ? 0 : lastNewline)
    : markdown
}
