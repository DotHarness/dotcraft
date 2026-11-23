import { Globe } from 'lucide-react'
import { useMemo, useState, type JSX, type ReactNode } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { DesktopPluginSurface } from '../../desktopPlugins/DesktopPluginSurface'
import { ToolDisclosure } from '../ToolDisclosure'
import type { SourceToolCall, SourceToolGroup } from './mcpSourceCalls'
import { extractSources, webSearchActivity, type Source, type SourceUsage, type WebSearchActivity } from './sources'
import { useSummaryState } from './store'
import { desktopSummaryServices, type SummaryServices } from './summaryServices'
import { GLYPH, Section } from './SummaryRows'
import { openSource, SourceIcon } from './SummarySources'
import styles from './SourcesTab.module.css'

const USAGE_KEYS: Record<SourceUsage, string> = {
  attached: 'summary.sourceAttached',
  provided: 'summary.sourceProvided',
  read: 'summary.sourceRead',
  loaded: 'summary.sourceLoaded'
}

const STATUS_KEYS: Record<Exclude<SourceToolCall['status'], 'completed'>, string> = {
  running: 'status.running',
  failed: 'status.failed',
  cancelled: 'summary.sourceCallCancelled'
}

export function SourcesTab({ threadId, workspacePath }: { threadId: string; workspacePath: string | null }): JSX.Element {
  const services = desktopSummaryServices
  const state = useSummaryState(services, threadId)
  const items = state?.items
  const sources = useMemo(() => extractSources(items ?? [], workspacePath), [items, workspacePath])
  const webSearch = useMemo(() => webSearchActivity(items ?? []), [items])
  const busy = useConversationStore((s) => s.turnStatus === 'running' || s.turnStatus === 'waitingInput')
  const context = useMemo(() => ({ workspacePath, threadId, busy }), [workspacePath, threadId, busy])

  return (
    <SourcesDetail
      services={services}
      threadId={threadId}
      sources={sources}
      webSearch={webSearch}
      ready={state?.ready === true}
      contributed={<DesktopPluginSurface name="summary.sources" context={context} />}
    />
  )
}

export function SourcesDetail({ services, threadId, sources, webSearch, ready, contributed }: {
  services: SummaryServices
  threadId: string
  sources: readonly Source[]
  webSearch: WebSearchActivity | null
  ready: boolean
  contributed?: ReactNode
}): JSX.Element {
  const t = useT()
  return (
    <div className={styles.panel} role="tabpanel" aria-label={t('summary.sources')} aria-busy={!ready}>
      {ready ? (
        <>
          <ul className={styles.entries}>
            {sources.map((source) => (
              <SourceEntry key={`${source.kind}:${source.id}`} services={services} threadId={threadId} source={source} />
            ))}
            {webSearch && <WebSearchEntry services={services} activity={webSearch} />}
          </ul>
          {contributed && <Section hideWhenEmpty>{contributed}</Section>}
          <p className={styles.empty}>{t('summary.sourcesEmpty')}</p>
        </>
      ) : <p className={styles.empty} role="status">{t('summary.sourcesLoading')}</p>}
    </div>
  )
}

function Entry({ icon, label, meta, onOpen, activity }: {
  icon: ReactNode
  label: string
  meta?: string | null
  onOpen?: () => void
  activity: ReactNode[]
}): JSX.Element {
  const head = (
    <>
      <span className={styles.icon} aria-hidden="true">{icon}</span>
      <span className={styles.text}>
        <span className={styles.label}>{label}</span>
        {meta ? <span className={styles.meta}>{meta}</span> : null}
      </span>
    </>
  )
  return (
    <li className={styles.entry}>
      {onOpen
        ? <button type="button" className={styles.head} onClick={onOpen}>{head}</button>
        : <div className={styles.head}>{head}</div>}
      {activity.length > 0 && <ul className={styles.activity}>{activity}</ul>}
    </li>
  )
}

function SourceEntry({ services, threadId, source }: { services: SummaryServices; threadId: string; source: Source }): JSX.Element {
  const t = useT()
  const activity = [
    ...(source.usage ?? []).map((usage) => <li key={usage} className={styles.note}>{t(USAGE_KEYS[usage])}</li>),
    ...(source.toolGroups ?? []).map((group) => <li key={`tool:${group.name}`}><ToolUses group={group} /></li>)
  ]
  return (
    <Entry
      icon={<SourceIcon services={services} source={source} threadId={threadId} />}
      label={source.title}
      meta={sourceMeta(source)}
      onOpen={openSource(services, source)}
      activity={activity}
    />
  )
}

function sourceMeta(source: Source): string | null {
  if (source.kind === 'web') return source.target
  if (source.kind === 'file' && source.id !== source.title) return source.id
  return null
}

function WebSearchEntry({ services, activity }: { services: SummaryServices; activity: WebSearchActivity }): JSX.Element {
  const t = useT()
  const { searches, queries, pages } = activity
  const rows: ReactNode[] = []
  if (searches > 0) {
    rows.push(
      <li key="searches">
        <Activity
          title={searches === 1 ? t('summary.sourceSearchedOnce') : t('summary.sourceSearchedMany', { count: searches })}
          expandable={queries.length > 0}
        >
          <ul className={styles.list}>{queries.map((query) => <li key={query}>{query}</li>)}</ul>
        </Activity>
      </li>
    )
  }
  if (pages.length > 0) {
    rows.push(
      <li key="pages">
        <Activity title={pages.length === 1 ? t('summary.sourcePagesOne') : t('summary.sourcePagesMany', { count: pages.length })}>
          <ul className={styles.list}>
            {pages.map((page) => (
              <li key={page.url}>
                <button type="button" className={styles.link} title={page.url} onClick={() => void services.navigation.openExternal(page.url)}>
                  {page.title}
                </button>
              </li>
            ))}
          </ul>
        </Activity>
      </li>
    )
  }
  return <Entry icon={<Globe {...GLYPH} />} label={t('summary.webSearch')} activity={rows} />
}

function ToolUses({ group }: { group: SourceToolGroup }): JSX.Element {
  const t = useT()
  const count = group.calls.length
  return (
    <Activity
      title={count === 1 ? t('summary.sourceToolUsedOnce', { name: group.name }) : t('summary.sourceToolUsedMany', { name: group.name, count })}
      expandable={group.calls.some(hasDetails)}
    >
      <ol className={styles.calls}>
        {group.calls.filter(hasDetails).map((call) => (
          <li key={`${call.turnId}:${call.callId}`}><CallDetails call={call} /></li>
        ))}
      </ol>
    </Activity>
  )
}

function Activity({ title, expandable = true, children }: { title: string; expandable?: boolean; children: ReactNode }): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  return (
    <ToolDisclosure title={title} expandable={expandable} expanded={expanded} onToggle={() => setExpanded((value) => !value)}>
      {children}
    </ToolDisclosure>
  )
}

function hasDetails(call: SourceToolCall): boolean {
  return call.status !== 'completed' || hasInput(call) || call.output !== undefined
}

function hasInput(call: SourceToolCall): boolean {
  const input = call.input
  if (input === undefined || input === null) return false
  return typeof input !== 'object' || Object.keys(input).length > 0
}

function CallDetails({ call }: { call: SourceToolCall }): JSX.Element {
  const t = useT()
  return (
    <>
      {call.status !== 'completed' && <p className={styles.status} data-status={call.status}>{t(STATUS_KEYS[call.status])}</p>}
      <dl className={styles.payloads}>
        {hasInput(call) && <Payload label={t('summary.sourceInput')} value={call.input} />}
        {call.output !== undefined && <Payload label={t('summary.sourceResult')} value={call.output} />}
      </dl>
    </>
  )
}

function Payload({ label, value }: { label: string; value: unknown }): JSX.Element {
  return (
    <div>
      <dt>{label}</dt>
      <dd><pre className={styles.payload}>{typeof value === 'string' ? value : JSON.stringify(value, null, 2)}</pre></dd>
    </div>
  )
}
