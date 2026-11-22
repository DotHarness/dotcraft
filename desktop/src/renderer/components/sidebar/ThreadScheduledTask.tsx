import { useEffect, useMemo, type JSX } from 'react'
import { Clock } from 'lucide-react'
import { create } from 'zustand'
import { useShallow } from 'zustand/react/shallow'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useAutomationsStore, type AutomationDefinition } from '../../stores/automationsStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import { automationNextRun } from '../../utils/automationNextRun'
import { ActionTooltip } from '../ui/ActionTooltip'
import { SidebarEntryDetailsActionRow } from './SidebarEntryDetailsActionRow'

const EMPTY_AUTOMATIONS: AutomationDefinition[] = []

export interface ThreadScheduledTaskSummary {
  automations: AutomationDefinition[]
  runOrigin: boolean
}

interface SecondaryAutomationsState {
  byConnectionKey: Record<string, AutomationDefinition[]>
  loadedScopeByKey: Record<string, string>
  setConnectionAutomations(connectionKey: string, scope: string, automations: AutomationDefinition[]): void
  upsertConnectionAutomation(connectionKey: string, automation: AutomationDefinition): void
  removeConnectionAutomation(connectionKey: string, automationId: string): void
  pruneConnections(activeKeys: ReadonlySet<string>): void
}

export const useSecondaryAutomationsStore = create<SecondaryAutomationsState>((set) => ({
  byConnectionKey: {},
  loadedScopeByKey: {},
  setConnectionAutomations(connectionKey, scope, automations) {
    set((state) => ({
      byConnectionKey: { ...state.byConnectionKey, [connectionKey]: automations },
      loadedScopeByKey: { ...state.loadedScopeByKey, [connectionKey]: scope }
    }))
  },
  upsertConnectionAutomation(connectionKey, automation) {
    set((state) => {
      const current = state.byConnectionKey[connectionKey] ?? []
      return {
        byConnectionKey: {
          ...state.byConnectionKey,
          [connectionKey]: [automation, ...current.filter((entry) => entry.id !== automation.id)]
        }
      }
    })
  },
  removeConnectionAutomation(connectionKey, automationId) {
    set((state) => {
      const current = state.byConnectionKey[connectionKey]
      if (!current) return state
      return {
        byConnectionKey: {
          ...state.byConnectionKey,
          [connectionKey]: current.filter((entry) => entry.id !== automationId)
        }
      }
    })
  },
  pruneConnections(activeKeys) {
    set((state) => {
      const keys = Object.keys(state.loadedScopeByKey)
      if (keys.every((key) => activeKeys.has(key))) return state
      const byConnectionKey: Record<string, AutomationDefinition[]> = {}
      const loadedScopeByKey: Record<string, string> = {}
      for (const key of keys) {
        if (!activeKeys.has(key)) continue
        byConnectionKey[key] = state.byConnectionKey[key] ?? EMPTY_AUTOMATIONS
        loadedScopeByKey[key] = state.loadedScopeByKey[key]
      }
      return { byConnectionKey, loadedScopeByKey }
    })
  }
}))

function isScheduledRunOrigin(originChannel: string): boolean {
  return originChannel.trim().toLowerCase() === 'automations'
}

function selectActiveThreadAutomations(automations: readonly AutomationDefinition[], threadId: string): AutomationDefinition[] {
  return automations
    .filter((entry) => entry.executionMode === 'thread' && entry.status === 'active' && entry.targetThreadId === threadId)
    .sort((left, right) => {
      const leftNext = left.nextRunAt ? Date.parse(left.nextRunAt) : Number.POSITIVE_INFINITY
      const rightNext = right.nextRunAt ? Date.parse(right.nextRunAt) : Number.POSITIVE_INFINITY
      if (leftNext !== rightNext) return leftNext - rightNext
      return left.name.localeCompare(right.name)
    })
}

export function hasThreadScheduledTask(summary: ThreadScheduledTaskSummary): boolean {
  return summary.automations.length > 0 || summary.runOrigin
}

export function useForegroundThreadScheduledTasks(thread: ThreadSummary, isSubAgent: boolean): ThreadScheduledTaskSummary {
  const automations = useAutomationsStore(
    useShallow((state) => isSubAgent ? EMPTY_AUTOMATIONS : selectActiveThreadAutomations(state.automations, thread.id))
  )
  const runOrigin = !isSubAgent && isScheduledRunOrigin(thread.originChannel)
  return useMemo(() => ({ automations, runOrigin }), [automations, runOrigin])
}

export function useSecondaryThreadScheduledTasks(
  thread: ThreadSummary,
  project: WorkspaceProjectSummary,
  isSubAgent: boolean
): ThreadScheduledTaskSummary {
  const connectionKey = project.connectionKey ?? ''
  const automations = useSecondaryAutomationsStore(
    useShallow((state) => {
      if (isSubAgent || !connectionKey) return EMPTY_AUTOMATIONS
      const list = state.byConnectionKey[connectionKey]
      return list ? selectActiveThreadAutomations(list, thread.id) : EMPTY_AUTOMATIONS
    })
  )
  const runOrigin = !isSubAgent && isScheduledRunOrigin(thread.originChannel)
  return useMemo(() => ({ automations, runOrigin }), [automations, runOrigin])
}

export function useSyncSecondaryProjectAutomations(projects: readonly WorkspaceProjectSummary[]): void {
  const targets = projects.filter(
    (project) =>
      project.loaded &&
      project.state === 'secondary' &&
      Boolean(project.connectionKey) &&
      project.capabilities?.automations === true
  )
  const signature = targets
    .map((project) => `${project.connectionKey}\u0000${project.connectionId ?? ''}`)
    .join('\u0001')

  useEffect(() => {
    const activeKeys = new Set(targets.map((project) => project.connectionKey!))
    useSecondaryAutomationsStore.getState().pruneConnections(activeKeys)

    let cancelled = false
    for (const project of targets) {
      const key = project.connectionKey!
      const scope = `${key}\u0000${project.connectionId ?? ''}`
      if (useSecondaryAutomationsStore.getState().loadedScopeByKey[key] === scope) continue
      void window.api.appServer
        .sendConnectionRequest(
          key,
          'automation/list',
          {},
          undefined,
          project.connectionId ? { connectionId: project.connectionId } : undefined
        )
        .then((result) => {
          if (cancelled) return
          const automations = (result as { automations?: AutomationDefinition[] } | null)?.automations ?? []
          useSecondaryAutomationsStore.getState().setConnectionAutomations(key, scope, automations)
        })
        .catch(() => {})
    }
    return () => {
      cancelled = true
    }
  }, [signature])

  useEffect(() => window.api.appServer.onNotificationRaw((payload) => {
    if (payload.foreground || payload.method !== 'automation/updated' || !payload.connectionKey) return
    const params = payload.params as { removed?: boolean; automationId?: string; automation?: AutomationDefinition } | null
    if (!params) return
    const store = useSecondaryAutomationsStore.getState()
    if (!(payload.connectionKey in store.loadedScopeByKey)) return
    if (params.removed && typeof params.automationId === 'string') {
      store.removeConnectionAutomation(payload.connectionKey, params.automationId)
    } else if (params.automation) {
      store.upsertConnectionAutomation(payload.connectionKey, params.automation)
    }
  }), [])
}

export function ThreadScheduledTaskGlyph({ summary }: { summary: ThreadScheduledTaskSummary }): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const primary = summary.automations[0]
  const nextRun = primary ? automationNextRun(primary.nextRunAt, Date.now(), locale) : null
  const ariaLabel = primary
    ? t('threadEntry.scheduledTaskNamed', { name: primary.name })
    : t('threadEntry.scheduledTask')
  const tooltipLabel = primary
    ? nextRun ? `${primary.name} · ${nextRun}` : ariaLabel
    : ariaLabel

  return (
    <ActionTooltip label={tooltipLabel} placement="top">
      <span className="dc-thread-scheduled-glyph" role="img" aria-label={ariaLabel}>
        <Clock size={14} strokeWidth={1.9} aria-hidden />
      </span>
    </ActionTooltip>
  )
}

export function ThreadScheduledTaskRows({
  automations,
  interactive
}: {
  automations: readonly AutomationDefinition[]
  interactive: boolean
}): JSX.Element | null {
  const locale = useLocale()
  const t = useT()
  if (automations.length === 0) return null

  return (
    <>
      {automations.map((automation) => {
        const nextRun = automationNextRun(automation.nextRunAt, Date.now(), locale)
        const text = nextRun ? `${automation.name} · ${nextRun}` : automation.name
        const ariaLabel = t('threadEntry.scheduledTaskNamed', { name: automation.name })
        if (!interactive) {
          return (
            <div key={automation.id} className="sidebar-entry-details-row">
              <Clock size={14} strokeWidth={1.8} aria-hidden />
              <span title={text}>{text}</span>
            </div>
          )
        }
        return (
          <SidebarEntryDetailsActionRow
            key={automation.id}
            icon={<Clock size={14} strokeWidth={1.8} aria-hidden />}
            label={text}
            title={text}
            ariaLabel={ariaLabel}
            affordance
            onClick={(event) => {
              event.stopPropagation()
              useUIStore.getState().setActiveMainView('automations')
              useAutomationsStore.getState().selectAutomation(automation.id)
            }}
          />
        )
      })}
    </>
  )
}
