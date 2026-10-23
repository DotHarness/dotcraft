import { Box, ChevronUp, Clock, FileText, Globe, Lightbulb, Link, Server } from 'lucide-react'
import { useEffect, useMemo, useState, type JSX } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { RobotAvatar } from '../../agents/RobotAvatar'
import { BackgroundTasksSection } from './BackgroundTasksSection'
import { EnvironmentSection } from './EnvironmentSection'
import type { SummaryAutomation, SummaryState } from './feed'
import { changeTotals, extractSources, hasWebSearch, workflowCallIds, type Source, type SourceKind } from './sources'
import { useSummaryState } from './store'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'

const SOURCE_LIMIT = 3
const AVATAR_LIMIT = 4

const SOURCE_GLYPHS: Record<SourceKind, typeof FileText> = {
  file: FileText,
  skill: Box,
  mcp: Server,
  web: Globe
}

function SubagentsRow({ services, state }: {
  services: SummaryServices
  state: SummaryState
}): JSX.Element {
  const t = useT()
  const working = state.subagents.filter((agent) => agent.state === 'working' || agent.state === 'waiting')
  const finished = state.subagents.filter((agent) => agent.state !== 'working' && agent.state !== 'waiting')
  const label = working.length > 0 ? t('summary.working', { count: working.length }) : t('summary.done', { count: finished.length })
  const meta = working.length > 0 && finished.length > 0 ? t('summary.done', { count: finished.length }) : null
  const shown = working.length > 0 ? working : finished
  const stack = shown.slice(0, AVATAR_LIMIT)
  const overflow = shown.length - stack.length

  return (
    <li>
      <button
        type="button"
        className="conversation-summary-row conversation-summary-row--action conversation-summary-row--subagents"
        aria-label={`${t('summary.openSubagents')}: ${meta ? `${label}, ${meta}` : label}`}
        onClick={() => services.navigation.openDetailPanel('subagents')}
      >
        <span className="conversation-summary-avatars" aria-hidden="true">
          {stack.map((agent) => (
            <span key={agent.childThreadId} className="conversation-summary-avatars__item">
              <RobotAvatar name={agent.nickname} size={20} />
            </span>
          ))}
          {overflow > 0 ? <span className="conversation-summary-avatars__more">+{overflow}</span> : null}
        </span>
        <span className="conversation-summary-row__label">{label}</span>
        {meta ? <span className="conversation-summary-row__inline">{meta}</span> : null}
      </button>
    </li>
  )
}

export function formatNextRun(nextRunAt: string, locale: string, now: Date = new Date()): string | null {
  const at = new Date(nextRunAt)
  if (Number.isNaN(at.getTime())) return null
  const time: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' }
  const sameDay = at.toDateString() === now.toDateString()
  const withinWeek = at.getTime() - now.getTime() < 6 * 24 * 60 * 60 * 1000
  const options: Intl.DateTimeFormatOptions = sameDay
    ? time
    : withinWeek
      ? { weekday: 'short', ...time }
      : { month: 'short', day: 'numeric', ...time }
  try {
    return new Intl.DateTimeFormat(locale, options).format(at)
  } catch {
    return at.toLocaleString()
  }
}

function automationNote(automation: SummaryAutomation, locale: string, paused: string): string | undefined {
  if (automation.paused) return paused
  return automation.nextRunAt ? formatNextRun(automation.nextRunAt, locale) ?? undefined : undefined
}

function workspaceName(workspacePath: string | null): string | null {
  const name = workspacePath?.replace(/[\\/]+$/, '').split(/[\\/]/).pop()
  return name || null
}

function SourceThumbnail({ services, path }: { services: SummaryServices; path: string }): JSX.Element {
  const [src, setSrc] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    setSrc(null)
    services.workspaces.readImageAttachment(path).then(
      (url) => { if (!cancelled) setSrc(url) },
      () => undefined
    )
    return () => { cancelled = true }
  }, [services, path])
  return src ? <img className="conversation-summary-thumbnail" src={src} alt="" draggable={false} /> : <FileText {...GLYPH} />
}

function SourceRows({ services, sources, webSearch }: {
  services: SummaryServices
  sources: readonly Source[]
  webSearch: boolean
}): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const rows = webSearch ? [...sources, null] : sources
  const visible = expanded ? rows : rows.slice(0, SOURCE_LIMIT)
  const open = (source: Source): (() => void) | undefined => {
    const target = source.target
    if (!target) return undefined
    if (source.kind === 'file') return () => void services.navigation.openFile(target)
    if (source.kind === 'web') return () => void services.navigation.openExternal(target)
    if (source.kind === 'skill') return () => services.navigation.openSkill(target)
    return undefined
  }
  return (
    <>
      {visible.map((source) => {
        if (!source) return <Row key="web-search" leading={<Globe {...GLYPH} />} label={t('summary.webSearch')} />
        const Glyph = SOURCE_GLYPHS[source.kind]
        return (
          <Row
            key={`${source.kind}:${source.id}`}
            leading={source.image && source.target
              ? <SourceThumbnail services={services} path={source.target} />
              : <Glyph {...GLYPH} />}
            label={source.title}
            title={source.kind === 'skill' || source.kind === 'mcp' ? source.title : source.id}
            onClick={open(source)}
          />
        )
      })}
      {rows.length > SOURCE_LIMIT ? (
        <Row
          leading={expanded ? <ChevronUp {...GLYPH} /> : <Link {...GLYPH} />}
          label={expanded ? t('summary.showLess') : t('summary.viewAll')}
          ariaExpanded={expanded}
          muted
          onClick={() => setExpanded((value) => !value)}
        />
      ) : null}
    </>
  )
}

export function SummaryPanel({
  services,
  threadId,
  workspacePath,
  variant
}: {
  services: SummaryServices
  threadId: string
  workspacePath: string | null
  variant: 'pinned' | 'popover'
}): JSX.Element | null {
  const state = useSummaryState(services, threadId)
  const locale = useLocale()
  const t = useT()
  const items = state?.items
  const sources = useMemo(() => extractSources(items ?? [], workspacePath), [items, workspacePath])
  const webSearch = useMemo(() => hasWebSearch(items ?? []), [items])
  const turnDiffs = state?.turnDiffs
  const changes = useMemo(() => changeTotals(items ?? [], turnDiffs), [items, turnDiffs])
  const workflowCalls = useMemo(() => workflowCallIds(items ?? []), [items])
  const header = (
    <header className="conversation-summary__header">{workspaceName(workspacePath) ?? t('summary.panelLabel')}</header>
  )

  if (!state?.ready) return <div className="conversation-summary" data-variant={variant}>{header}</div>

  const plan = state.plan
  const planLabel = plan ? plan.title || t('summary.progressCount', { done: plan.done, total: plan.total }) : ''
  const empty = !changes && !plan && state.subagents.length === 0 && state.automations.length === 0
    && state.terminals.length === 0 && state.workflows.length === 0 && sources.length === 0 && !webSearch

  return (
    <div className="conversation-summary" data-variant={variant}>
      {header}
      {empty ? <p className="conversation-summary__empty">{t('summary.empty')}</p> : null}

      {plan ? (
        <Section title={plan.title ? t('summary.plan') : t('summary.progress')}>
          <Row
            leading={<Lightbulb {...GLYPH} />}
            label={planLabel}
            title={planLabel}
            onClick={() => services.navigation.openDetailPanel('plan')}
          />
        </Section>
      ) : null}

      {state.subagents.length > 0 ? (
        <Section title={t('summary.subagents')}>
          <SubagentsRow services={services} state={state} />
        </Section>
      ) : null}

      <BackgroundTasksSection
        services={services}
        threadId={threadId}
        terminals={state.terminals}
        workflows={state.workflows}
        workflowCalls={workflowCalls}

      />

      {state.automations.length > 0 ? (
        <Section title={t('summary.scheduled')}>
          {state.automations.map((automation) => (
            <Row
              key={automation.id}
              leading={<Clock {...GLYPH} />}
              label={automation.name}
              inline={automationNote(automation, locale, t('summary.paused'))}
              title={automation.name}
              onClick={() => services.navigation.openAutomation(automation.id)}
            />
          ))}
        </Section>
      ) : null}

      {sources.length > 0 || webSearch ? (
        <Section title={t('summary.sources')}>
          <SourceRows services={services} sources={sources} webSearch={webSearch} />
        </Section>
      ) : null}

      <EnvironmentSection services={services} threadId={threadId} workspacePath={workspacePath} changes={changes} />
    </div>
  )
}
