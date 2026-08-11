import { useEffect, useMemo, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { selectWorkflowRunEntry, useWorkflowRunStore } from '../../stores/workflowRunStore'
import { WorkflowStatusGlyph, formatWorkflowElapsed, workflowTone } from './workflowPresentation'

interface WorkflowToolCardProps {
  threadId: string
  runId: string
  createdAt?: string
}

export function WorkflowToolCard({ threadId, runId, createdAt }: WorkflowToolCardProps): JSX.Element {
  const t = useT()
  const [expanded, setExpanded] = useState(true)
  const entries = useWorkflowRunStore((state) => state.entries)
  const load = useWorkflowRunStore((state) => state.load)
  const entry = selectWorkflowRunEntry(entries, threadId, runId)
  const run = entry?.run
  useEffect(() => { void load(threadId, runId) }, [load, runId, threadId])

  const elapsed = formatWorkflowElapsed(run?.startedAt ?? createdAt, run?.completedAt)
  const visiblePhases = useMemo(
    () => run?.phases.filter((phase) => phase.agents.length > 0 || phase.status !== 'pending') ?? [],
    [run]
  )

  const openDetails = (): void => {
    const tabId = useViewerTabStore.getState().openWorkflow({
      threadId, runId, initialLabel: t('workflow.tab')
    })
    useUIStore.getState().setActiveViewerTab(tabId)
  }

  const status = run?.status ?? 'running'
  const name = run?.name ?? runId
  return (
    <section className="dc-workflow-tool-card" data-expanded={expanded ? 'true' : 'false'}>
      <button
        type="button"
        className="dc-workflow-tool-card__toggle"
        aria-expanded={expanded}
        onClick={() => setExpanded((value) => !value)}
      >
        <span className={status === 'running' ? 'tool-running-gradient-text' : undefined}>
          {t(status === 'running' ? 'workflow.card.running' : 'workflow.card.finished', { name })}
        </span>
        {elapsed && <span className="dc-workflow-tool-card__elapsed">{elapsed}</span>}
        <ChevronDown size={13} className="dc-workflow-tool-card__chevron" aria-hidden />
      </button>
      {expanded && (
        <div className="dc-workflow-tool-card__body">
          {visiblePhases.map((phase) => (
            <div key={phase.name} className="dc-workflow-tool-card__phase" data-tone={workflowTone(phase.status)}>
              <span className="dc-workflow-tool-card__status"><WorkflowStatusGlyph status={phase.status} /></span>
              <button type="button" className="dc-quiet-action dc-workflow-tool-card__phase-title" onClick={openDetails}>
                {phase.name}
              </button>
              <span className={phase.status === 'running' ? 'dc-workflow-tool-card__detail tool-running-gradient-text' : 'dc-workflow-tool-card__detail'}>
                {phase.detail ?? ''}
              </span>
            </div>
          ))}
          {entry?.error && <p className="dc-workflow-tool-card__error">{entry.error}</p>}
        </div>
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
