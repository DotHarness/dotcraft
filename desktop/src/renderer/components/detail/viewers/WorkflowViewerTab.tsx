import { useEffect, useState } from 'react'
import { Square } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useThreadStore } from '../../../stores/threadStore'
import { useUIStore } from '../../../stores/uiStore'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { selectWorkflowRunEntry, useWorkflowRunStore } from '../../../stores/workflowRunStore'
import { Button } from '../../ui/Button'
import { WorkflowStatusGlyph, formatWorkflowElapsed, formatWorkflowTokens, workflowTone } from '../../workflow/workflowPresentation'
import type { WorkflowViewerTab as WorkflowViewerTabDescriptor } from '../../../../shared/viewer/types'

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
          <button type="button" className="dc-workflow-runtime__stop" disabled={stopping} onClick={() => void requestStop()}>
            <Square size={8} fill="currentColor" aria-hidden />
            {t(stopping ? 'workflow.stopping' : 'workflow.stop')}
          </button>
        )}
      </header>
      {run.error && <div className="dc-workflow-runtime__error">{run.error}</div>}
      <div className="dc-workflow-runtime__phases">
        {run.phases.map((phase) => (
          <section key={phase.name} className="dc-workflow-runtime-phase" data-tone={workflowTone(phase.status)}>
            <header className="dc-workflow-runtime-phase__header">
              <span><WorkflowStatusGlyph status={phase.status} /></span>
              <h4>{phase.name}</h4>
              <p className={phase.status === 'running' ? 'tool-running-gradient-text' : undefined}>{phase.detail ?? ''}</p>
            </header>
            <div className="dc-workflow-runtime-phase__agents">{phase.agents.map(renderAgent)}</div>
          </section>
        ))}
        {run.unphasedAgents.length > 0 && (
          <section className="dc-workflow-runtime-phase">
            <header className="dc-workflow-runtime-phase__header"><span /><h4>{t('workflow.otherAgents')}</h4><p /></header>
            <div className="dc-workflow-runtime-phase__agents">{run.unphasedAgents.map(renderAgent)}</div>
          </section>
        )}
      </div>
    </div>
  )
}
