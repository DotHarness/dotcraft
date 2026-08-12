import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { runWithoutAppNavigationRecording } from '../../stores/appNavigationStore'
import { selectWorkflowRunEntry, useWorkflowRunStore } from '../../stores/workflowRunStore'
import { Button } from '../ui/Button'
import { WorkflowStatusGlyph, useWorkflowElapsed, workflowTone } from './workflowPresentation'

interface WorkflowToolCardProps {
  threadId: string
  runId: string
  createdAt?: string
}

export function WorkflowToolCard({ threadId, runId, createdAt }: WorkflowToolCardProps): JSX.Element {
  const t = useT()
  const entries = useWorkflowRunStore((state) => state.entries)
  const load = useWorkflowRunStore((state) => state.load)
  const entry = selectWorkflowRunEntry(entries, threadId, runId)
  const run = entry?.run
  useEffect(() => { void load(threadId, runId) }, [load, runId, threadId])

  const elapsed = useWorkflowElapsed(run?.startedAt ?? createdAt, run?.completedAt)
  const visiblePhases = run?.phases ?? []

  const openDetails = (): void => {
    const tabId = useViewerTabStore.getState().openWorkflow({
      threadId, runId, initialLabel: t('workflow.tab')
    })
    useUIStore.getState().setActiveViewerTab(tabId)
  }

  const name = run?.name ?? runId
  const status = run?.status ?? 'running'
  return (
    <section
      className="dc-workflow-tool-card"
      aria-label={t(status === 'running' ? 'workflow.card.running' : 'workflow.card.finished', { name })}
    >
      <div className="dc-workflow-tool-card__header">
        <div className="dc-workflow-tool-card__identity">
          <span className="dc-workflow-tool-card__eyebrow">{t('workflow.tab')}</span>
          <span className="dc-workflow-tool-card__title">{name}</span>
        </div>
        {elapsed && <span className="dc-workflow-tool-card__elapsed">{elapsed}</span>}
      </div>
      {visiblePhases.length > 0 && (
        <div className="dc-workflow-tool-card__phases">
          {visiblePhases.map((phase) => (
            <div key={phase.name} className="dc-workflow-tool-card__phase" data-tone={workflowTone(phase.status)}>
              <span className="dc-workflow-tool-card__status"><WorkflowStatusGlyph status={phase.status} /></span>
              <button
                type="button"
                className="dc-quiet-action dc-workflow-tool-card__phase-action"
                aria-label={phase.name}
                onClick={openDetails}
              >
                <span className="dc-workflow-tool-card__phase-title">{phase.name}</span>
                <span className={phase.status === 'running' ? 'dc-workflow-tool-card__detail tool-running-gradient-text' : 'dc-workflow-tool-card__detail'}>
                  {phase.detail ?? ''}
                </span>
              </button>
            </div>
          ))}
        </div>
      )}
      {entry?.error && (
        <p className="dc-workflow-tool-card__error">
          <span>{t('workflow.loadFailed')}</span>
          <Button variant="secondary" size="sm" onClick={() => void load(threadId, runId)}>
            {t('workflow.retry')}
          </Button>
        </p>
      )}
    </section>
  )
}

export function parseWorkflowRunId(toolName: string, result?: string): string | null {
  if (toolName.toLowerCase() !== 'workflow' || !result) return null
  try {
    const value = JSON.parse(result) as { runId?: unknown }
    return typeof value.runId === 'string' ? value.runId : null
  } catch { return null }
}

export function parseWorkflowLaunch(toolName: string, result?: string): { runId: string; name: string } | null {
  if (toolName.toLowerCase() !== 'workflow' || !result) return null
  try {
    const value = JSON.parse(result) as { runId?: unknown; name?: unknown; status?: unknown }
    if (typeof value.runId !== 'string' || typeof value.name !== 'string' || value.status !== 'running') return null
    return { runId: value.runId, name: value.name }
  } catch { return null }
}

export function autoOpenWorkflowLaunch(
  threadId: string,
  toolName: string,
  result: string | undefined,
  success: boolean | undefined
): boolean {
  const launch = success ? parseWorkflowLaunch(toolName, result) : null
  if (!launch) return false
  let opened = false
  runWithoutAppNavigationRecording(() => {
    if (!useUIStore.getState().maybeAutoShowForReason(`workflow:${threadId}:${launch.runId}`)) return
    const tabId = useViewerTabStore.getState().openWorkflow({
      threadId,
      runId: launch.runId,
      initialLabel: launch.name
    })
    useUIStore.getState().setActiveViewerTab(tabId)
    opened = true
  })
  return opened
}
