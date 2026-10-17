import type { ConversationTurn } from '../types/conversation'

export interface WebCitationSource {
  url: string
  label: string
}

export type WebCitationSources = ReadonlyMap<string, WebCitationSource>

export type WebCitationMode = 'display' | 'copy'

const MARKER_START = String.fromCharCode(0xe200)
const MARKER_END = String.fromCharCode(0xe201)
const MARKER_SEPARATOR = String.fromCharCode(0xe202)
const MARKER = new RegExp(`${MARKER_START}([^${MARKER_END}]*)${MARKER_END}`, 'g')
const PARTIAL_MARKER = new RegExp(`${MARKER_START}[^${MARKER_END}]*$`)
const MARKER_CHAR = `[${MARKER_START}${MARKER_END}${MARKER_SEPARATOR}]`
const ANY_MARKER_CHAR = new RegExp(MARKER_CHAR)
const STRAY_MARKER_CHARS = new RegExp(MARKER_CHAR, 'g')

export const NO_WEB_CITATION_SOURCES: WebCitationSources = new Map()

export function hasWebCitationMarkers(text: string | undefined): boolean {
  return text != null && ANY_MARKER_CHAR.test(text)
}

export function collectWebCitationSources(turns: readonly ConversationTurn[]): WebCitationSources {
  const sources = new Map<string, WebCitationSource>()
  for (const turn of turns) {
    for (const item of turn.items) {
      for (const entry of item.webSearch?.results ?? []) {
        const refId = typeof entry.ref_id === 'string' ? entry.ref_id.trim() : ''
        const url = typeof entry.url === 'string' ? toHttpUrl(entry.url) : null
        if (!refId || !url) continue
        const domain = typeof entry.domain === 'string' && entry.domain.trim()
          ? entry.domain.trim()
          : url.hostname
        sources.set(refId, { url: url.href, label: domain.replace(/^www\./i, '') })
      }
    }
  }
  return sources
}

export function formatWebCitations(
  text: string,
  sources: WebCitationSources,
  mode: WebCitationMode,
  streaming = false
): string {
  if (!hasWebCitationMarkers(text)) return text
  const complete = streaming ? text.replace(PARTIAL_MARKER, '') : text
  return complete
    .replace(MARKER, (_marker, body: string) => formatCitation(body, sources, mode))
    .replace(STRAY_MARKER_CHARS, '')
}

export function stripWebCitations(text: string, streaming = false): string {
  return formatWebCitations(text, NO_WEB_CITATION_SOURCES, 'copy', streaming)
}

function formatCitation(body: string, sources: WebCitationSources, mode: WebCitationMode): string {
  const [kind, ...refIds] = body.split(MARKER_SEPARATOR)
  if (kind?.trim() !== 'cite') return ''
  const cited: WebCitationSource[] = []
  for (const refId of refIds) {
    const source = sources.get(refId.trim())
    if (source && !cited.some((entry) => entry.url === source.url)) cited.push(source)
  }
  if (cited.length === 0) return ''
  if (mode === 'copy') return `(${cited.map((source) => markdownLink(source.label, source.url)).join(', ')})`
  const [first] = cited
  return markdownLink(cited.length > 1 ? `${first.label} +${cited.length - 1}` : first.label, first.url)
}

function markdownLink(label: string, url: string): string {
  return `[${label.replace(/[\\[\]]/g, '\\$&')}](<${url}>)`
}

function toHttpUrl(value: string): URL | null {
  try {
    const url = new URL(value.trim())
    return url.protocol === 'http:' || url.protocol === 'https:' ? url : null
  } catch {
    return null
  }
}
