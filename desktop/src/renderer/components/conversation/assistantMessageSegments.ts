import { isPendingVisualizationReference, parseVisualizationReferenceLine, type VisualizationContentReference } from './visualizationParser'

export type AssistantMessageSegment =
  | { kind: 'markdown'; start: number; text: string }
  | { kind: 'visualization'; start: number; reference: VisualizationContentReference }

type VisualizationReferenceMode = 'render' | 'strip' | 'keep'

interface Fence { char: string; length: number }

export function splitAssistantMessage(text: string, { streaming, visualizations }: { streaming: boolean; visualizations: VisualizationReferenceMode }): AssistantMessageSegment[] {
  const segments: AssistantMessageSegment[] = []
  const lines = text.split('\n')
  let markdown = ''
  let markdownStart = 0
  let fence: Fence | null = null
  let offset = 0

  const flush = (): void => {
    if (markdown.trim()) segments.push({ kind: 'markdown', start: markdownStart, text: markdown })
    markdown = ''
  }

  for (let lineIndex = 0; lineIndex < lines.length; lineIndex++) {
    const rawLine = lines[lineIndex]
    const start = offset
    const last = lineIndex === lines.length - 1
    offset += rawLine.length + 1
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine
    const trimmed = line.trimStart()
    const marker = /^(`{3,}|~{3,})/.exec(trimmed)?.[1]
    const closes = (open: Fence): boolean =>
      marker !== undefined && marker[0] === open.char && marker.length >= open.length && trimmed.slice(marker.length).trim() === ''

    if (fence) {
      if (closes(fence)) fence = null
    } else if (marker) {
      fence = { char: marker[0], length: marker.length }
    } else if (visualizations !== 'keep') {
      if (streaming && last && isPendingVisualizationReference(line)) continue
      const reference = parseVisualizationReferenceLine(line)
      if (reference) {
        if (visualizations === 'render') {
          flush()
          segments.push({ kind: 'visualization', start, reference })
        }
        continue
      }
    }
    if (!markdown) markdownStart = start
    markdown += last ? rawLine : `${rawLine}\n`
  }

  flush()
  return segments
}

export function assistantMessageCopyText(text: string): string {
  const segments = splitAssistantMessage(text, { streaming: false, visualizations: 'strip' })
  if (segments.length === 1 && segments[0].kind === 'markdown' && segments[0].text === text) return text
  return segments
    .flatMap(segment => segment.kind === 'markdown' ? [segment.text] : [])
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

export function parseVisualizationContentReferences(markdown: string): VisualizationContentReference[] {
  return splitAssistantMessage(markdown, { streaming: false, visualizations: 'render' })
    .flatMap(segment => segment.kind === 'visualization' ? [segment.reference] : [])
}
