import { Circle, CircleCheck, CirclePause, CirclePlay, LoaderCircle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { AutomationDefinition } from '../../types/automation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Button } from '../ui/Button'

export function AutomationStatusControl({ automation, running, disabled, disabledReason, onToggle }: {
  automation: AutomationDefinition; running: boolean; disabled: boolean; disabledReason?: string; onToggle(): void
}): JSX.Element {
  const t = useT()
  const pending = useAutomationsStore(state => !!state.pendingActions[automation.id])
  const paused = automation.status === 'paused'
  const label = t(paused ? 'automation.resume' : 'automation.pause')
  if (running || automation.status === 'completed') return <span className="dc-automation-row-status" aria-label={t(running ? 'automation.run.running' : 'automation.status.completed')}>
    {running ? <LoaderCircle size={17} className="animate-spin-custom" /> : <CircleCheck size={17} />}
  </span>
  return <ActionTooltip label={label} disabledReason={disabled ? disabledReason : undefined}>
    <Button className="dc-automation-row-status" variant="ghost" size="iconSm" disabled={disabled || pending} aria-label={label}
      onClick={event => { event.stopPropagation(); onToggle() }} onDragStart={event => event.preventDefault()}>
      {paused ? <CirclePlay size={17} /> : <>
        <Circle size={17} className="dc-automation-status-rest" />
        <CirclePause size={17} className="dc-automation-status-hover" />
      </>}
    </Button>
  </ActionTooltip>
}
