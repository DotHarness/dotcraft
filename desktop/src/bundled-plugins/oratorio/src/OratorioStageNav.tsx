import { Check } from 'lucide-react'
import type { KeyboardEvent as ReactKeyboardEvent } from 'react'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { lifecycleStageForTask, TASK_STAGES } from './oratorio-workflow'

export function OratorioStageNav({
  task,
  selectedStage,
  onStageChange,
}: {
  task: OratorioTask
  selectedStage: TaskStage
  onStageChange: (stage: TaskStage) => void
}) {
  const lifecycleStage = lifecycleStageForTask(task)
  const lifecycleIndex = TASK_STAGES.findIndex((stage) => stage.id === lifecycleStage)

  function handleKeyDown(event: ReactKeyboardEvent<HTMLButtonElement>, index: number): void {
    let nextIndex: number | undefined
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') nextIndex = (index + 1) % TASK_STAGES.length
    if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') nextIndex = (index - 1 + TASK_STAGES.length) % TASK_STAGES.length
    if (event.key === 'Home') nextIndex = 0
    if (event.key === 'End') nextIndex = TASK_STAGES.length - 1
    if (nextIndex === undefined) return
    event.preventDefault()
    const nextStage = TASK_STAGES[nextIndex].id
    onStageChange(nextStage)
    window.requestAnimationFrame(() => document.getElementById(`ora-stage-tab-${nextStage}`)?.focus())
  }

  return (
    <ol className="ora-stages dc-scrollbar-stable" aria-label="Task workflow" role="tablist" data-lifecycle-stage={lifecycleStage}>
      {TASK_STAGES.map((stage, index) => {
        const progress = index < lifecycleIndex ? 'complete' : index === lifecycleIndex ? 'current' : 'pending'
        const selected = stage.id === selectedStage
        return (
          <li className={`ora-stage ${progress}${selected ? ' selected' : ''}`} key={stage.id} role="presentation">
            <button
              type="button"
              id={`ora-stage-tab-${stage.id}`}
              role="tab"
              aria-selected={selected}
              aria-controls={`ora-stage-panel-${stage.id}`}
              tabIndex={selected ? 0 : -1}
              onClick={() => onStageChange(stage.id)}
              onKeyDown={(event) => handleKeyDown(event, index)}
            >
              <span className="ora-stage__marker-row" aria-hidden="true">
                <span className="ora-stage__node">{progress === 'complete' ? <Check size={12} strokeWidth={2.5} /> : null}</span>
              </span>
              <span className="ora-stage__copy"><strong>{stage.label}</strong></span>
            </button>
          </li>
        )
      })}
    </ol>
  )
}
