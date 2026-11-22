import type { DesktopPluginThreadSurfaceContext } from '@dotcraft/plugin'
import { Box, ChevronUp, Clock, FileText, Globe, Lightbulb, Link, Server } from 'lucide-react'
import { useEffect, useLayoutEffect, useMemo, useRef, useState, type JSX, type ReactNode, type RefObject } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { useSkillIconUrl } from '../../../utils/skillIcon'
import { useMcpSourceIconUrl } from '../../../utils/mcpPluginIcon'
import { RobotAvatar } from '../../agents/RobotAvatar'
import { DesktopPluginSurface } from '../../desktopPlugins/DesktopPluginSurface'
import { useDesktopPluginRegistry } from '../../../plugins/desktopPluginRegistry'
import { BackgroundTasksSection } from './BackgroundTasksSection'
import { LinkedChatsSection } from './LinkedChatsSection'
import { useOwnedThread } from '../../../stores/ownedThreadsStore'
import { ServersSection } from './ServersSection'
import { ContextSection } from './ContextSection'
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
        data-muted={working.length === 0 || undefined}
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
        {meta ? <span className="conversation-summary-row__meta">{meta}</span> : null}
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

function SourceIcon({ services, source, threadId }: { services: SummaryServices; source: Source; threadId: string }): JSX.Element {
  const skillIconUrl = useSkillIconUrl(source.kind === 'skill' ? source.id : null)
  const mcpIconUrl = useMcpSourceIconUrl(source.kind === 'mcp' ? source.id : null, source.pluginId, threadId)
  const [failedSrc, setFailedSrc] = useState<string | null>(null)
  const customIconUrl = skillIconUrl || mcpIconUrl
  if (customIconUrl && failedSrc !== customIconUrl) {
    return (
      <img
        className="conversation-summary-skill-icon"
        src={customIconUrl}
        alt=""
        draggable={false}
        onError={() => setFailedSrc(customIconUrl)}
      />
    )
  }
  if (source.image && source.target) return <SourceThumbnail services={services} path={source.target} />
  const Glyph = SOURCE_GLYPHS[source.kind]
  return <Glyph {...GLYPH} />
}

const SOURCE_ROWS = ':scope > [data-summary-sources] > li, :scope > [data-dotcraft-plugin-surface] > li'

function useSourceRowLimit(list: RefObject<HTMLUListElement | null>, expanded: boolean): number {
  const [count, setCount] = useState(0)
  useLayoutEffect(() => {
    const element = list.current
    if (!element) return
    const apply = (): void => {
      const rows = element.querySelectorAll<HTMLElement>(SOURCE_ROWS)
      rows.forEach((row, index) => {
        row.toggleAttribute('data-source-overflow', !expanded && index >= SOURCE_LIMIT)
      })
      setCount(rows.length)
    }
    apply()
    const observer = new MutationObserver(apply)
    observer.observe(element, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [list, expanded])
  return count
}

function SourcesSection({ services, threadId, sources, webSearch, contributed }: {
  services: SummaryServices
  threadId: string
  sources: readonly Source[]
  webSearch: boolean
  contributed: ReactNode
}): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(false)
  const list = useRef<HTMLUListElement>(null)
  const count = useSourceRowLimit(list, expanded)
  const rows = webSearch ? [...sources, null] : sources
  const open = (source: Source): (() => void) | undefined => {
    const target = source.target
    if (!target) return undefined
    if (source.kind === 'file') return () => void services.navigation.openFile(target)
    if (source.kind === 'web') return () => void services.navigation.openExternal(target)
    if (source.kind === 'skill') return () => services.navigation.openSkill(target)
    return undefined
  }
  return (
    <Section title={t('summary.sources')} hideWhenEmpty={sources.length === 0 && !webSearch} listRef={list}>
      <div data-summary-sources style={{ display: 'contents' }}>
        {rows.map((source) => {
          if (!source) return <Row key="web-search" leading={<Globe {...GLYPH} />} label={t('summary.webSearch')} />
          return (
            <Row
              key={`${source.kind}:${source.id}`}
              leading={<SourceIcon services={services} source={source} threadId={threadId} />}
              label={source.title}
              title={source.kind === 'skill' || source.kind === 'mcp' ? source.title : source.id}
              onClick={open(source)}
            />
          )
        })}
      </div>
      {contributed}
      {count > SOURCE_LIMIT ? (
        <Row
          leading={expanded ? <ChevronUp {...GLYPH} /> : <Link {...GLYPH} />}
          label={expanded ? t('summary.showLess') : t('summary.viewAll')}
          ariaExpanded={expanded}
          muted
          onClick={() => setExpanded((value) => !value)}
        />
      ) : null}
    </Section>
  )
}

export function SummaryPanel({
  services,
  threadId,
  workspacePath,
  busy,
  variant
}: {
  services: SummaryServices
  threadId: string
  workspacePath: string | null
  busy: boolean
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
  const pluginContext = useMemo<DesktopPluginThreadSurfaceContext>(
    () => ({ workspacePath, threadId, busy }),
    [workspacePath, threadId, busy]
  )
  const pluginSources = useDesktopPluginRegistry((registry) => registry.surfaces.some((surface) => surface.surface === 'summary.sources'))
  const owned = useOwnedThread(threadId)
  const header = (
    <header className="conversation-summary__header">
      {owned ? owned.title : workspaceName(workspacePath) ?? t('summary.panelLabel')}
    </header>
  )

  if (owned) {
    return (
      <div className="conversation-summary" data-variant={variant}>
        {header}
        <LinkedChatsSection threadId={threadId} />
        {state?.ready ? (
          <BackgroundTasksSection
            services={services}
            threadId={threadId}
            terminals={state.terminals}
            workflows={state.workflows}
            workflowCalls={workflowCalls}
          />
        ) : null}
        <DesktopPluginSurface name="summary.sections" context={pluginContext} />
        <ContextSection services={services} threadId={threadId} />
      </div>
    )
  }

  if (!state?.ready) return <div className="conversation-summary" data-variant={variant}>{header}</div>

  const plan = state.plan
  const planLabel = plan ? plan.title || t('summary.progressCount', { done: plan.done, total: plan.total }) : ''

  return (
    <div className="conversation-summary" data-variant={variant}>
      {header}

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

      <LinkedChatsSection threadId={threadId} />

      <ServersSection threadId={threadId} />

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
              meta={automationNote(automation, locale, t('summary.paused'))}
              title={automation.name}
              onClick={() => services.navigation.openAutomation(automation.id, automation.name)}
            />
          ))}
        </Section>
      ) : null}

      {sources.length > 0 || webSearch || pluginSources ? (
        <SourcesSection
          services={services}
          threadId={threadId}
          sources={sources}
          webSearch={webSearch}
          contributed={<DesktopPluginSurface name="summary.sources" context={pluginContext} />}
        />
      ) : null}

      <DesktopPluginSurface name="summary.sections" context={pluginContext} />

      <ContextSection services={services} threadId={threadId} />
      <EnvironmentSection
        services={services}
        threadId={threadId}
        workspacePath={workspacePath}
        roots={workspacePath ? services.workspaces.roots(workspacePath, threadId) : []}
        changes={changes}
      />
    </div>
  )
}
