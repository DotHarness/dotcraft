import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem, WebSearchAction, WebSearchInfo } from '../types/conversation'
import { extractPartialJsonStringValue } from './toolCallDisplay'
import { toWebSearchResultRows, type WebSearchResultRow } from './webToolDisplay'

export type WebSearchRowState = 'running' | 'completed' | 'failed'

export interface WebSearchDisplay {
  state: WebSearchRowState
  label: string
  detail: string
  sources: WebSearchResultRow[]
  errorMessage: string
}

const SITE_FILTER = /\bsite:([^\s]+)/giu
const OR_OPERATOR = /\bOR\b/gu

export function getWebSearchDetail(info: Pick<WebSearchInfo, 'action' | 'query'>): string {
  const fromAction = info.action ? formatActionDetail(info.action).trim() : ''
  return fromAction || info.query.trim()
}

export function getWebSearchState(item: Pick<ConversationItem, 'webSearch' | 'status'>): WebSearchRowState {
  const status = item.webSearch?.status ?? 'completed'
  if (status !== 'inProgress') return status
  return item.status === 'completed' ? 'completed' : 'running'
}

export function formatWebSearchLabel(
  item: Pick<ConversationItem, 'webSearch' | 'status'>,
  locale: AppLocale
): string {
  const display = buildDisplay(getWebSearchState(item), item.webSearch ? getWebSearchDetail(item.webSearch) : '', [], '', locale)
  return display.label + display.detail
}

export function getWebSearchDisplay(
  item: Pick<ConversationItem, 'webSearch' | 'status' | 'errorMessage'>,
  locale: AppLocale
): WebSearchDisplay {
  return buildDisplay(
    getWebSearchState(item),
    item.webSearch ? getWebSearchDetail(item.webSearch) : '',
    toWebSearchResultRows(item.webSearch?.results ?? []),
    item.errorMessage ?? '',
    locale
  )
}

export function getWebSearchToolDisplay(
  item: Pick<ConversationItem, 'arguments' | 'argumentsPreview' | 'result' | 'errorMessage' | 'structuredResult'>,
  { running, success }: { running: boolean; success: boolean },
  locale: AppLocale
): WebSearchDisplay {
  const argument = item.arguments?.query
  const query = typeof argument === 'string'
    ? argument
    : item.argumentsPreview ? extractPartialJsonStringValue(item.argumentsPreview, 'query') ?? '' : ''
  const structured = item.structuredResult as { results?: unknown } | null | undefined
  const results = !running && Array.isArray(structured?.results) ? structured.results : []
  return buildDisplay(
    running ? 'running' : success ? 'completed' : 'failed',
    getWebSearchDetail({ action: { type: 'search', query }, query }),
    toWebSearchResultRows(results),
    item.errorMessage ?? item.result ?? '',
    locale
  )
}

function buildDisplay(
  state: WebSearchRowState,
  detail: string,
  sources: WebSearchResultRow[],
  errorMessage: string,
  locale: AppLocale
): WebSearchDisplay {
  const label = translate(locale, state === 'running' ? 'toolCall.webSearch.searching' : 'toolCall.webSearch.searched')
  return {
    state,
    label: state === 'failed' ? translate(locale, 'toolCall.failed', { label }) : label,
    detail: detail ? translate(locale, 'toolCall.webSearch.detail', { detail }) : '',
    sources: state === 'completed' ? sources : [],
    errorMessage: state === 'failed' ? errorMessage.trim() : ''
  }
}

function formatActionDetail(action: WebSearchAction): string {
  switch (action.type) {
    case 'search':
      return formatSearchDetail(action.query, action.queries)
    case 'openPage':
      return action.url ?? ''
    case 'findInPage':
      if (action.pattern && action.url) return `'${action.pattern}' in ${action.url}`
      return action.pattern ? `'${action.pattern}'` : action.url ?? ''
    case 'other':
      return ''
  }
}

function formatSearchDetail(query: string | undefined, queries: string[] | undefined): string {
  const single = query?.trim() ?? ''
  if (single) return formatSiteQuery(single)
  const first = queries?.map((entry) => entry.trim()).find((entry) => entry.length > 0) ?? ''
  return queries && queries.length > 1 && first ? `${formatSiteQuery(first)} ...` : formatSiteQuery(first)
}

function formatSiteQuery(query: string): string {
  const sites: string[] = []
  const rest = query.replace(SITE_FILTER, (match, value: string) => {
    const site = siteHostname(value)
    if (site == null) return match
    if (!sites.includes(site)) sites.push(site)
    return ''
  })
  if (sites.length === 0) return query
  const text = rest.replace(OR_OPERATOR, ' ').replace(/\s+/gu, ' ').trim()
  return text.length === 0 ? query : `${text} | ${sites.join(' · ')}`
}

function siteHostname(value: string): string | null {
  try {
    return new URL(`https://${value}`).hostname.replace(/^www\./u, '')
  } catch {
    return null
  }
}
