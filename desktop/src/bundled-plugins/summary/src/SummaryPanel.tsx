import { AgentAvatar, type DesktopPluginHost } from '@dotcraft/plugin'
import { Box, ChevronUp, Clock, FileText, Globe, Lightbulb, Link, Server } from 'lucide-react'
import { useEffect, useMemo, useState, type JSX } from 'react'
import { BackgroundTasksSection } from './BackgroundTasksSection'
import { EnvironmentSection } from './EnvironmentSection'
import type { SummaryAutomation, SummaryState } from './feed'
import { fill, stringsFor, type SummaryStrings } from './i18n'
import { changeTotals, extractSources, hasWebSearch, workflowCallIds, type Source, type SourceKind } from './sources'
import { useSummaryState } from './store'
import { GLYPH, Row, Section } from './SummaryRows'

const SOURCE_LIMIT = 3
const AVATAR_LIMIT = 4

const SOURCE_GLYPHS: Record<SourceKind, typeof FileText> = {
  file: FileText,
  skill: Box,
  mcp: Server,
  web: Globe
}

function SubagentsRow({ host, state, strings }: {
  host: DesktopPluginHost
  state: SummaryState
  strings: SummaryStrings
}): JSX.Element {
  const working = state.subagents.filter((agent) => agent.state === 'working' || agent.state === 'waiting')
  const finished = state.subagents.filter((agent) => agent.state !== 'working' && agent.state !== 'waiting')
  const label = working.length > 0 ? fill(strings.working, { count: working.length }) : fill(strings.done, { count: finished.length })
  const meta = working.length > 0 && finished.length > 0 ? fill(strings.done, { count: finished.length }) : null
  const shown = working.length > 0 ? working : finished
  const stack = shown.slice(0, AVATAR_LIMIT)
  const overflow = shown.length - stack.length

  return (
    <li>
      <button
        type="button"
        className="conversation-summary-row conversation-summary-row--action conversation-summary-row--subagents"
        aria-label={`${strings.openSubagents}: ${meta ? `${label}, ${meta}` : label}`}
        onClick={() => host.navigation.openDetailPanel('subagents')}
      >
        <span className="conversation-summary-avatars" aria-hidden="true">
          {stack.map((agent) => (
            <span key={agent.childThreadId} className="conversation-summary-avatars__item">
              <AgentAvatar name={agent.nickname} size={20} />
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

function automationNote(automation: SummaryAutomation, locale: string, strings: SummaryStrings): string | undefined {
  if (automation.paused) return strings.paused
  return automation.nextRunAt ? formatNextRun(automation.nextRunAt, locale) ?? undefined : undefined
}

function workspaceName(workspacePath: string | null): string | null {
  const name = workspacePath?.replace(/[\\/]+$/, '').split(/[\\/]/).pop()
  return name || null
}

function SourceThumbnail({ host, path }: { host: DesktopPluginHost; path: string }): JSX.Element {
  const [src, setSrc] = useState<string | null>(null)
  useEffect(() => {
    let cancelled = false
    setSrc(null)
    host.workspaces.readImageAttachment(path).then(
      (url) => { if (!cancelled) setSrc(url) },
      () => undefined
    )
    return () => { cancelled = true }
  }, [host, path])
  return src ? <img className="conversation-summary-thumbnail" src={src} alt="" draggable={false} /> : <FileText {...GLYPH} />
}

function SourceRows({ host, sources, webSearch, strings }: {
  host: DesktopPluginHost
  sources: readonly Source[]
  webSearch: boolean
  strings: SummaryStrings
}): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const rows = webSearch ? [...sources, null] : sources
  const visible = expanded ? rows : rows.slice(0, SOURCE_LIMIT)
  const open = (source: Source): (() => void) | undefined => {
    const target = source.target
    if (!target) return undefined
    if (source.kind === 'file') return () => void host.navigation.openFile(target)
    if (source.kind === 'web') return () => void host.navigation.openExternal(target)
    if (source.kind === 'skill') return () => host.navigation.openSkill(target)
    return undefined
  }
  return (
    <>
      {visible.map((source) => {
        if (!source) return <Row key="web-search" leading={<Globe {...GLYPH} />} label={strings.webSearch} />
        const Glyph = SOURCE_GLYPHS[source.kind]
        return (
          <Row
            key={`${source.kind}:${source.id}`}
            leading={source.image && source.target
              ? <SourceThumbnail host={host} path={source.target} />
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
          label={expanded ? strings.showLess : strings.viewAll}
          ariaExpanded={expanded}
          muted
          onClick={() => setExpanded((value) => !value)}
        />
      ) : null}
    </>
  )
}

export function SummaryPanel({
  host,
  threadId,
  workspacePath,
  variant
}: {
  host: DesktopPluginHost
  threadId: string
  workspacePath: string | null
  variant: 'pinned' | 'popover'
}): JSX.Element | null {
  const state = useSummaryState(host, threadId)
  const locale = host.environment.locale
  const strings = stringsFor(locale)
  const items = state?.items
  const sources = useMemo(() => extractSources(items ?? [], workspacePath), [items, workspacePath])
  const webSearch = useMemo(() => hasWebSearch(items ?? []), [items])
  const turnDiffs = state?.turnDiffs
  const changes = useMemo(() => changeTotals(items ?? [], turnDiffs), [items, turnDiffs])
  const workflowCalls = useMemo(() => workflowCallIds(items ?? []), [items])
  const header = (
    <header className="conversation-summary__header">{workspaceName(workspacePath) ?? strings.panelLabel}</header>
  )

  if (!state?.ready) return <div className="conversation-summary" data-variant={variant}>{header}</div>

  const plan = state.plan
  const planLabel = plan ? plan.title || fill(strings.progressCount, { done: plan.done, total: plan.total }) : ''
  const empty = !changes && !plan && state.subagents.length === 0 && state.automations.length === 0
    && state.terminals.length === 0 && state.workflows.length === 0 && sources.length === 0 && !webSearch

  return (
    <div className="conversation-summary" data-variant={variant}>
      {header}
      {empty ? <p className="conversation-summary__empty">{strings.empty}</p> : null}

      {plan ? (
        <Section title={plan.title ? strings.plan : strings.progress}>
          <Row
            leading={<Lightbulb {...GLYPH} />}
            label={planLabel}
            title={planLabel}
            onClick={() => host.navigation.openDetailPanel('plan')}
          />
        </Section>
      ) : null}

      {state.subagents.length > 0 ? (
        <Section title={strings.subagents}>
          <SubagentsRow host={host} state={state} strings={strings} />
        </Section>
      ) : null}

      <BackgroundTasksSection
        host={host}
        threadId={threadId}
        terminals={state.terminals}
        workflows={state.workflows}
        workflowCalls={workflowCalls}
        strings={strings}
      />

      {state.automations.length > 0 ? (
        <Section title={strings.scheduled}>
          {state.automations.map((automation) => (
            <Row
              key={automation.id}
              leading={<Clock {...GLYPH} />}
              label={automation.name}
              inline={automationNote(automation, locale, strings)}
              title={automation.name}
              onClick={() => host.navigation.openAutomation(automation.id)}
            />
          ))}
        </Section>
      ) : null}

      {sources.length > 0 || webSearch ? (
        <Section title={strings.sources}>
          <SourceRows host={host} sources={sources} webSearch={webSearch} strings={strings} />
        </Section>
      ) : null}

      <EnvironmentSection host={host} threadId={threadId} workspacePath={workspacePath} changes={changes} strings={strings} />
    </div>
  )
}
