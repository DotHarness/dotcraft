import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import type { ToolKindSource } from './toolKind'

const TITLE_LIMIT = 80

export type NodeReplSurface = 'browser' | 'chrome' | 'computer'

type NodeReplSource = ToolKindSource & Partial<Pick<ConversationItem, 'structuredResult' | 'type' | 'pluginNamespace'>>

export function isNodeReplToolCall(item: NodeReplSource): boolean {
  return item.source?.kind === 'Mcp'
    && item.source.sourceId === 'node_repl'
    && (item.source.sourceToolId === 'NodeReplJs' || item.toolName === 'NodeReplJs')
}

export function getNodeReplSurface(item: NodeReplSource): NodeReplSurface | null {
  if (!isNodeReplToolCall(item)) return null
  const result = item.structuredResult as { surface?: unknown } | null | undefined
  if (result?.surface === 'computer' || result?.surface === 'chrome' || result?.surface === 'browser') {
    return result.surface
  }
  return null
}

export function normalizeNodeReplTitle(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const title = value.trim().replace(/\s+/g, ' ')
  if (!title) return null
  return title.length > TITLE_LIMIT ? `${title.slice(0, TITLE_LIMIT)}…` : title
}

export function formatNodeReplSurfaceLabel(surface: NodeReplSurface | null, locale: AppLocale): string {
  switch (surface) {
    case 'browser': return translate(locale, 'toolCall.nodeRepl.usedBrowser')
    case 'chrome': return translate(locale, 'toolCall.nodeRepl.usedChrome')
    case 'computer': return translate(locale, 'toolCall.nodeRepl.usedComputer')
    default: return translate(locale, 'toolCall.nodeRepl.ranJavaScript')
  }
}

export function formatNodeReplLabel(item: NodeReplSource, title: unknown, locale: AppLocale): string {
  return normalizeNodeReplTitle(title) ?? formatNodeReplSurfaceLabel(getNodeReplSurface(item), locale)
}
