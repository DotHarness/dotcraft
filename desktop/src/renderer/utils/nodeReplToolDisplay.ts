import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { resolveCoreToolRenderPlan } from './toolRendererRegistry'
import type { ToolKindSource } from './toolKind'

const TITLE_LIMIT = 80

export type NodeReplSurface = 'browser' | 'chrome' | 'computer'

export function getNodeReplSurface(item: ToolKindSource): NodeReplSurface | null {
  const plan = resolveCoreToolRenderPlan(item as ConversationItem)
  return plan?.family === 'nodeRepl' ? plan.options.surface as NodeReplSurface : null
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

export function formatNodeReplLabel(item: ToolKindSource, title: unknown, locale: AppLocale): string {
  return normalizeNodeReplTitle(title) ?? formatNodeReplSurfaceLabel(getNodeReplSurface(item), locale)
}
