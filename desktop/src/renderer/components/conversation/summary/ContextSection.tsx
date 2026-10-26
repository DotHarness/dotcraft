import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { useEffect, useMemo, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { Button } from '../../ui/Button'
import { DisclosureChevron } from '../../ui/DisclosureChevron'
import { Skeleton } from '../../ui/Skeleton'
import { buildContextBreakdownView, type ContextBreakdownCategory, type ContextRow } from './contextBreakdown'
import type { SummaryContextState, SummaryServices } from './summaryServices'
import { setContextExpanded, useSummaryView } from './view'

const SOON_SHARE = 0.05
const PATH_BUDGET = 24
const PATH_MIN_HEAD = 3
const REVEAL_DELAY_MS = 220

let handledReveal = 0

type BreakdownResult =
  | { status: 'loading' }
  | { status: 'ready'; categories: readonly ContextBreakdownCategory[] }
  | { status: 'empty' }
  | { status: 'failed' }

function useContextState(services: SummaryServices, threadId: string): SummaryContextState {
  const [state, setState] = useState(() => services.context.state(threadId))
  useEffect(() => {
    setState(services.context.state(threadId))
    return services.context.onChange(threadId, setState)
  }, [services, threadId])
  return state
}

type WireCategory = NonNullable<
  NonNullable<ClientRequestMethods['thread/contextUsage/read']['result']['breakdown']>['categories']
>[number]

function toCategory(category: WireCategory): ContextBreakdownCategory {
  return {
    id: category.id ?? '',
    tokens: category.tokens ?? 0,
    onDemand: category.onDemand === true,
    items: category.items?.map((item) => ({ name: item.name ?? '', tokens: item.tokens ?? 0 }))
  }
}

function useBreakdown(services: SummaryServices, threadId: string, enabled: boolean, refreshKey: string): BreakdownResult {
  const [result, setResult] = useState<BreakdownResult>({ status: 'loading' })
  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    services.appServer.request('thread/contextUsage/read', { threadId }).then(
      (response) => {
        if (cancelled) return
        const categories = response.breakdown?.categories
        setResult(categories ? { status: 'ready', categories: categories.map(toCategory) } : { status: 'empty' })
      },
      () => {
        if (!cancelled) setResult((previous) => (previous.status === 'ready' ? previous : { status: 'failed' }))
      }
    )
    return () => {
      cancelled = true
    }
  }, [services, threadId, enabled, refreshKey])
  return result
}

export function ContextSection({ services, threadId }: { services: SummaryServices; threadId: string }): JSX.Element | null {
  const t = useT()
  const { contextExpanded, contextReveal } = useSummaryView()
  const { usage, running, canCompact } = useContextState(services, threadId)
  const available = usage !== null && usage.contextWindow > 0
  const breakdown = useBreakdown(services, threadId, available && contextExpanded, running ? 'running' : String(usage?.tokens))
  const sectionRef = useRef<HTMLElement>(null)

  useEffect(() => {
    if (!contextReveal || contextReveal.id <= handledReveal || breakdown.status === 'loading') return
    const timer = setTimeout(() => {
      const section = sectionRef.current
      const scroller = section?.closest<HTMLElement>('.conversation-summary-pinned, .conversation-summary-popover')
      if (!section || !scroller) return
      handledReveal = contextReveal.id
      scroller.scrollTop = section.offsetTop
      if (contextReveal.focus) section.querySelector('button')?.focus({ preventScroll: true })
    }, REVEAL_DELAY_MS)
    return () => clearTimeout(timer)
  }, [contextReveal, breakdown.status])

  if (!available) return null

  const contextWindow = usage.contextWindow
  const used = Math.min(usage.tokens, contextWindow)
  const percent = Math.round((used / contextWindow) * 100)

  return (
    <section ref={sectionRef} className="conversation-summary-section" aria-label={t('summary.context.title')}>
      <h3 className="conversation-summary-section__title">{t('summary.context.title')}</h3>
      <ul className="conversation-summary-rows">
        <li>
          <button
            type="button"
            className="conversation-summary-row conversation-summary-row--action"
            aria-expanded={contextExpanded}
            onClick={() => setContextExpanded(!contextExpanded)}
          >
            <span className="conversation-summary-row__label">{t('summary.context.used', { percent })}</span>
            <span className="conversation-summary-row__meta">
              {t('summary.context.tokens', { used: formatCompactCount(used), total: formatCompactCount(contextWindow) })}
              <DisclosureChevron expanded={contextExpanded} direction="reveal" />
            </span>
          </button>
        </li>
      </ul>
      {contextExpanded ? (
        <ContextDetail
          breakdown={breakdown}
          usedTokens={used}
          contextWindow={contextWindow}
          autoCompactThreshold={usage.autoCompactThreshold}
          canCompact={canCompact}
          onCompact={() => void services.context.compact(threadId)}
        />
      ) : null}
    </section>
  )
}

function shortenPath(path: string): string {
  const name = path.split(/[\\/]/).pop() ?? path
  if (name === path || path.length <= PATH_BUDGET) return path
  return `${path.slice(0, Math.max(PATH_MIN_HEAD, PATH_BUDGET - name.length - 1))}…${name}`
}

function seriesColor(row: ContextRow): string {
  return row.series === null ? 'var(--chart-series-other)' : `var(--chart-series-${row.series})`
}

function ContextDetail({
  breakdown,
  usedTokens,
  contextWindow,
  autoCompactThreshold,
  canCompact,
  onCompact
}: {
  breakdown: BreakdownResult
  usedTokens: number
  contextWindow: number
  autoCompactThreshold: number
  canCompact: boolean
  onCompact: () => void
}): JSX.Element {
  const t = useT()
  const view = useMemo(
    () => (breakdown.status === 'ready' ? buildContextBreakdownView(breakdown.categories, usedTokens) : null),
    [breakdown, usedTokens]
  )
  const bufferTokens = Math.max(0, contextWindow - autoCompactThreshold)
  const freeTokens = Math.max(0, contextWindow - usedTokens - bufferTokens)
  const untilAutoCompact = Math.max(0, autoCompactThreshold - usedTokens)
  const share = (tokens: number): string => `${(tokens / contextWindow) * 100}%`
  const percent = (tokens: number): string => {
    const value = (tokens / contextWindow) * 100
    return value > 0 && value < 0.1 ? '<0.1%' : `${value.toFixed(1)}%`
  }
  const label = (id: string): string => t(`summary.context.category.${id}`)

  return (
    <div className="conversation-summary-context">
      <div className="conversation-summary-context__meter" aria-hidden="true">
        {view ? (
          <>
            {view.rows
              .filter((row) => row.series !== null)
              .map((row) => (
                <span key={row.id} style={{ width: share(row.tokens), background: seriesColor(row) }} />
              ))}
            {view.foldedTokens > 0 ? (
              <span style={{ width: share(view.foldedTokens), background: 'var(--chart-series-other)' }} />
            ) : null}
            {view.sinceLastUpdate > 0 ? <span data-kind="since" style={{ width: share(view.sinceLastUpdate) }} /> : null}
          </>
        ) : (
          <span style={{ width: share(usedTokens), background: 'var(--chart-series-other)' }} />
        )}
        <span className="conversation-summary-context__rest" />
        {bufferTokens > 0 ? <span data-kind="buffer" style={{ width: share(bufferTokens) }} /> : null}
      </div>

      {breakdown.status === 'loading' ? (
        <div className="conversation-summary-context__loading" aria-label={t('summary.context.loading')}>
          {[70, 56, 62].map((width) => (
            <Skeleton key={width} width={`${width}%`} height={12} />
          ))}
        </div>
      ) : null}
      {breakdown.status === 'empty' || breakdown.status === 'failed' ? (
        <p className="conversation-summary-context__note">
          {t(breakdown.status === 'empty' ? 'summary.context.empty' : 'summary.context.failed')}
        </p>
      ) : null}

      {view ? (
        <ul className="conversation-summary-context__legend">
          {view.rows.map((row) => (
            <LegendItem
              key={row.id}
              label={label(row.id)}
              tokens={row.tokens}
              share={percent(row.tokens)}
              swatch={seriesColor(row)}
              items={row.items}
            />
          ))}
          {view.sinceLastUpdate > 0 ? (
            <LegendItem
              label={label('sinceLastUpdate')}
              tokens={view.sinceLastUpdate}
              share={percent(view.sinceLastUpdate)}
              swatch="since"
            />
          ) : null}
          {bufferTokens > 0 ? (
            <LegendItem label={t('summary.context.buffer')} tokens={bufferTokens} share={percent(bufferTokens)} swatch="buffer" />
          ) : null}
          <LegendItem label={t('summary.context.free')} tokens={freeTokens} share={percent(freeTokens)} swatch="free" />
          {view.onDemand.map((row) => (
            <LegendItem
              key={`onDemand-${row.id}`}
              label={t('summary.context.onDemandCategory', { name: label(row.id) })}
              tokens={row.tokens}
              share="—"
              swatch="free"
              items={row.items}
              onDemand
            />
          ))}
        </ul>
      ) : null}

      <div className="conversation-summary-context__foot">
        <span>
          {untilAutoCompact < contextWindow * SOON_SHARE
            ? t('summary.context.compactsSoon')
            : t('summary.context.untilAutoCompact', { tokens: formatCompactCount(untilAutoCompact) })}
        </span>
        <Button type="button" variant="secondary" size="sm" disabled={!canCompact} onClick={onCompact}>
          {t('summary.context.compact')}
        </Button>
      </div>
    </div>
  )
}

const NAMED_SWATCHES = new Set(['since', 'buffer', 'free'])

function LegendItem({
  label,
  tokens,
  share,
  swatch,
  items = [],
  onDemand = false
}: {
  label: string
  tokens: number
  share: string
  swatch: string
  items?: ContextRow['items']
  onDemand?: boolean
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const expandable = items.length > 0
  const named = NAMED_SWATCHES.has(swatch)
  const content = (
    <>
      <span
        className="conversation-summary-context__swatch"
        data-kind={named ? swatch : undefined}
        style={named ? undefined : { background: swatch }}
      />
      <span className="conversation-summary-context__name">
        <span className="conversation-summary-context__label">{label}</span>
        {expandable ? <DisclosureChevron expanded={expanded} /> : null}
      </span>
      <span className="conversation-summary-context__value">{formatCompactCount(tokens)}</span>
      <span className="conversation-summary-context__share">{share}</span>
    </>
  )
  return (
    <li data-on-demand={onDemand || undefined}>
      {expandable ? (
        <button
          type="button"
          className="conversation-summary-context__row conversation-summary-context__row--action"
          aria-expanded={expanded}
          onClick={() => setExpanded(!expanded)}
        >
          {content}
        </button>
      ) : (
        <div className="conversation-summary-context__row">{content}</div>
      )}
      {expandable && expanded ? (
        <ul className="conversation-summary-context__legend" data-nested>
          {items.map((item) => (
            <li key={item.name}>
              <div className="conversation-summary-context__row">
                <span className="conversation-summary-context__swatch" />
                <span className="conversation-summary-context__name" title={item.name}>
                  <span className="conversation-summary-context__label">{shortenPath(item.name)}</span>
                </span>
                <span className="conversation-summary-context__value">{formatCompactCount(item.tokens)}</span>
                <span className="conversation-summary-context__share" />
              </div>
            </li>
          ))}
        </ul>
      ) : null}
    </li>
  )
}
