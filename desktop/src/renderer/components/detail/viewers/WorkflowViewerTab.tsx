import { useEffect, useId, useState, type ReactNode } from 'react'
import { Square } from 'lucide-react'
import type { WorkflowPhaseView } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { useThreadStore } from '../../../stores/threadStore'
import { useUIStore } from '../../../stores/uiStore'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { selectWorkflowRunEntry, useWorkflowRunStore } from '../../../stores/workflowRunStore'
import { Button } from '../../ui/Button'
import { IconButton } from '../../ui/IconButton'
import { ToolCollapseChevron } from '../../conversation/ToolDisclosure'
import { WorkflowStatusGlyph, formatWorkflowElapsed, formatWorkflowPhaseMetrics, formatWorkflowTokens, workflowTone } from '../../workflow/workflowPresentation'
import type { WorkflowViewerTab as WorkflowViewerTabDescriptor } from '../../../../shared/viewer/types'

function WorkflowPhaseSection({
  phase,
  renderAgent
}: {
  phase: WorkflowPhaseView
  renderAgent: (agent: WorkflowPhaseView['agents'][number]) => ReactNode
}): JSX.Element {
  const contentId = useId()
  const [expanded, setExpanded] = useState(
    phase.status === 'running' || phase.status === 'failed' || phase.status === 'stopped'
  )
  const [hovered, setHovered] = useState(false)
  const metrics = formatWorkflowPhaseMetrics(phase)

  return (
    <section className="dc-workflow-runtime-phase" data-tone={workflowTone(phase.status)}>
      <button
        type="button"
        className="dc-workflow-runtime-phase__header"
        aria-expanded={expanded}
        aria-controls={contentId}
        onClick={() => setExpanded((value) => !value)}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
      >
        <span className="dc-workflow-runtime-phase__marker"><WorkflowStatusGlyph status={phase.status} /></span>
        <span className="dc-workflow-runtime-phase__summary">
          <span className="dc-workflow-runtime-phase__label">{phase.name}</span>
          <span className={phase.status === 'running' ? 'dc-workflow-runtime-phase__detail tool-running-gradient-text' : 'dc-workflow-runtime-phase__detail'}>
            {phase.detail ?? ''}
          </span>
          <ToolCollapseChevron expanded={expanded} visible={hovered || expanded} />
        </span>
        {!expanded && metrics && <span className="dc-workflow-runtime-phase__metrics">{metrics}</span>}
      </button>
      <div
        id={contentId}
        className="dc-workflow-runtime-phase__collapse"
        data-expanded={expanded ? 'true' : 'false'}
        aria-hidden={!expanded}
      >
        <div className="dc-workflow-runtime-phase__collapse-inner" inert={!expanded ? true : undefined}>
          <div className="dc-workflow-runtime-phase__agents">{phase.agents.map(renderAgent)}</div>
        </div>
      </div>
    </section>
  )
}

export function WorkflowViewerTab({ tabId }: { tabId: string }): JSX.Element {
  const t = useT()
  const currentThreadId = useViewerTabStore((state) => state.currentThreadId)
  const tab = useViewerTabStore((state) => currentThreadId
    ? state.getThreadState(currentThreadId).tabs.find((candidate): candidate is WorkflowViewerTabDescriptor =>
        candidate.id === tabId && candidate.kind === 'workflow')
    : undefined)
  const entries = useWorkflowRunStore((state) => state.entries)
  const load = useWorkflowRunStore((state) => state.load)
  const stop = useWorkflowRunStore((state) => state.stop)
  const [stopping, setStopping] = useState(false)
  const entry = tab ? selectWorkflowRunEntry(entries, tab.threadId, tab.runId) : undefined
  const run = entry?.run

  useEffect(() => { if (tab) void load(tab.threadId, tab.runId) }, [load, tab])
  if (!tab) return <div className="dc-workflow-runtime__notice">{t('workflow.missing')}</div>
  if (!run) return (
    <div className={entry?.error ? 'dc-workflow-runtime__notice dc-workflow-runtime__notice--recoverable' : 'dc-workflow-runtime__notice'}>
      <span>{entry?.error ? t('workflow.loadFailed') : t('workflow.loading')}</span>
      {entry?.error && (
        <Button variant="secondary" size="sm" onClick={() => void load(tab.threadId, tab.runId)}>
          {t('workflow.retry')}
        </Button>
      )}
    </div>
  )

  const openAgent = (childThreadId?: string): void => {
    if (!childThreadId) return
    useThreadStore.getState().setActiveThreadId(childThreadId)
    useUIStore.getState().setActiveMainView('conversation')
  }

  const requestStop = async (): Promise<void> => {
    setStopping(true)
    try { await stop(tab.threadId, tab.runId) } finally { setStopping(false) }
  }

  const renderAgent = (agent: typeof run.unphasedAgents[number]): JSX.Element => {
    const elapsed = formatWorkflowElapsed(agent.startedAt ?? agent.requestedAt, agent.completedAt)
    return (
      <div key={agent.operationId} className="dc-workflow-runtime-agent" data-tone={workflowTone(agent.status)}>
        <span className="dc-workflow-runtime-agent__status"><WorkflowStatusGlyph status={agent.status} /></span>
        <button
          type="button"
          className="dc-quiet-action dc-workflow-runtime-agent__label"
          disabled={!agent.childThreadId}
          onClick={() => openAgent(agent.childThreadId)}
        >
          {agent.label}
        </button>
        <span className="dc-workflow-runtime-agent__metrics">
          {formatWorkflowTokens(agent.inputTokens, agent.outputTokens)} tok · {agent.toolCallCount} tools
          {agent.status !== 'running' && elapsed ? ` · ${elapsed}` : ''}
        </span>
      </div>
    )
  }

  return (
    <div className="dc-workflow-runtime">
      <header className="dc-workflow-runtime__header">
        <div>
          <h3>{run.name}</h3>
          <p>{run.description}</p>
        </div>
        {run.controls.canStop && (
          <IconButton
            icon={<Square size={10} fill="currentColor" aria-hidden />}
            label={t(stopping ? 'workflow.stopping' : 'workflow.stop')}
            tooltipLabel={t(stopping ? 'workflow.stopping' : 'workflow.stop')}
            size={28}
            disabled={stopping}
            onClick={() => void requestStop()}
          />
        )}
      </header>
      {run.error && <div className="dc-workflow-runtime__error">{run.error}</div>}
      <div className="dc-workflow-runtime__phases">
        {run.phases.map((phase) => (
          <WorkflowPhaseSection key={phase.name} phase={phase} renderAgent={renderAgent} />
        ))}
        {run.unphasedAgents.length > 0 && (
          <section className="dc-workflow-runtime-phase">
            <div className="dc-workflow-runtime-phase__static-header"><span /><h4>{t('workflow.otherAgents')}</h4><p /></div>
            <div className="dc-workflow-runtime-phase__agents">{run.unphasedAgents.map(renderAgent)}</div>
          </section>
        )}
      </div>
    </div>
  )
}
