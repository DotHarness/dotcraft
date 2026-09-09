import { CirclePause, CirclePlay, MoreHorizontal, Play, Trash2 } from 'lucide-react'
import { useState, type DragEvent } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import type { AutomationDefinition } from '../../types/automation'
import { Button } from '../ui/Button'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { AutomationStatusControl } from './AutomationStatusControl'
import { AutomationTaskTiming } from './AutomationTaskTiming'

const emptyRuns: ReturnType<typeof useAutomationsStore.getState>['runs'][string] = []

export function AutomationTaskRow({ automation, selected, disabled, disabledReason, actionPending, onSelect, onToggle, onRun, onDelete, onDragStart }: {
  automation: AutomationDefinition
  selected: boolean
  disabled: boolean
  disabledReason?: string
  actionPending: boolean
  onSelect(): void
  onToggle(): void
  onRun(): void
  onDelete(): void
  onDragStart(event: DragEvent<HTMLElement>): void
}): JSX.Element {
  const t = useT()
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)
  const runs = useAutomationsStore(state => state.runs[automation.id] ?? emptyRuns)
  const schedulePending = useAutomationsStore(state => !!state.pendingActions[automation.id])
  const runtime = useThreadStore(state => state.runtimeSnapshots)
  const running = runs.some(run => run.status === 'running' || run.status === 'queued'
    || !!(run.threadId && runtime.get(run.threadId)?.running))
  const unread = runs.some(run => !run.readAt)
  const completed = automation.status === 'completed'
  const blocked = disabled || schedulePending || actionPending

  return <article draggable data-status={automation.status} data-selected={selected || undefined}
    data-running={running || undefined} data-menu-open={menu != null || undefined} onDragStart={onDragStart}>
    <AutomationStatusControl automation={automation} running={running} disabled={disabled || actionPending}
      disabledReason={disabledReason} onToggle={onToggle} />
    <button type="button" className="dc-automation-task-main" onClick={onSelect}>
      <strong>{automation.name}</strong>
      <AutomationTaskTiming automation={automation} running={running} />
    </button>
    <span className="dc-automation-task-actions">
      {unread ? <span className="dc-automation-task-unread" aria-label={t('automation.unread')} /> : null}
      <Button variant="ghost" size="iconSm" className="dc-automation-task-menu" aria-label={t('automation.actions')}
        aria-haspopup="menu" aria-expanded={menu != null} onClick={event => {
          event.stopPropagation()
          const rect = event.currentTarget.getBoundingClientRect()
          setMenu({ x: rect.right, y: rect.bottom })
        }}><MoreHorizontal size={16} /></Button>
    </span>
    {menu ? <ContextMenu position={menu} onClose={() => setMenu(null)} items={[
      ...(!completed ? [
        { label: t('automation.runNow'), icon: <Play size={15} />, disabled: blocked, title: disabledReason, onClick: onRun },
        { label: t(automation.status === 'paused' ? 'automation.resume' : 'automation.pause'),
          icon: automation.status === 'paused' ? <CirclePlay size={15} /> : <CirclePause size={15} />,
          disabled: blocked, title: disabledReason, onClick: onToggle }
      ] : []),
      { label: t('automation.delete'), icon: <Trash2 size={15} />, danger: true, disabled: blocked, title: disabledReason, onClick: onDelete }
    ]} /> : null}
  </article>
}
