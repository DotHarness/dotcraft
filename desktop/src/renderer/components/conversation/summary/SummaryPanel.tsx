import type { DesktopPluginThreadSurfaceContext } from '@dotcraft/plugin'
import { Clock, Lightbulb } from 'lucide-react'
import { useMemo, type JSX } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
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
import { changeTotals, extractSources, webSearchActivity, workflowCallIds } from './sources'
import { useSummaryState } from './store'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'
import { SummarySources } from './SummarySources'

const AVATAR_LIMIT = 4

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

export function SummaryPanel({
  services,
  threadId,
  workspacePath,
  busy,
  variant,
  onOpenSources
}: {
  services: SummaryServices
  threadId: string
  workspacePath: string | null
  busy: boolean
  variant: 'pinned' | 'popover'
  onOpenSources?: () => void
}): JSX.Element | null {
  const state = useSummaryState(services, threadId)
  const locale = useLocale()
  const t = useT()
  const items = state?.items
  const sources = useMemo(() => extractSources(items ?? [], workspacePath), [items, workspacePath])
  const webSearch = useMemo(() => webSearchActivity(items ?? []) != null, [items])
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
        <LinkedChatsSection services={services} threadId={threadId} />
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
        <Section
          sectionKey="plan"
          title={plan.title ? t('summary.plan') : t('summary.progress')}
          collapsedSummary={plan.total > 0 ? `${plan.done}/${plan.total}` : undefined}
        >
          <Row
            leading={<Lightbulb {...GLYPH} />}
            label={planLabel}
            title={planLabel}
            onClick={() => services.navigation.openDetailPanel('plan')}
          />
        </Section>
      ) : null}

      {state.subagents.length > 0 ? (
        <Section sectionKey="subagents" title={t('summary.subagents')} count={state.subagents.length}>
          <SubagentsRow services={services} state={state} />
        </Section>
      ) : null}

      <LinkedChatsSection services={services} threadId={threadId} />

      <ServersSection threadId={threadId} />

      <BackgroundTasksSection
        services={services}
        threadId={threadId}
        terminals={state.terminals}
        workflows={state.workflows}
        workflowCalls={workflowCalls}

      />

      {state.automations.length > 0 ? (
        <Section sectionKey="scheduled" title={t('summary.scheduled')} count={state.automations.length}>
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
        <SummarySources
          services={services}
          threadId={threadId}
          sources={sources}
          webSearch={webSearch}
          contributed={<DesktopPluginSurface name="summary.sources" context={pluginContext} />}
          onViewAll={() => {
            services.navigation.openDetailPanel('sources')
            onOpenSources?.()
          }}
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
