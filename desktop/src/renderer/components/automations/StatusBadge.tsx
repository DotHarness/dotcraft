import { useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import type { AutomationTaskStatus } from '../../stores/automationsStore'
import { CircleCheck, Clock, Loader2, TriangleAlert } from 'lucide-react'

interface StatusConfig {
  label: string
  color: string
  icon: JSX.Element
}

const spinnerKeyframes = `
@keyframes automations-spin {
  to { transform: rotate(360deg); }
}
`

function SpinnerIcon(): JSX.Element {
  return (
    <>
      <style>{spinnerKeyframes}</style>
      <Loader2
        size={14}
        strokeWidth={1.5}
        aria-hidden
        style={{ animation: 'automations-spin 1s linear infinite' }}
      />
    </>
  )
}

const STATUS_LABEL_KEY: Record<AutomationTaskStatus, MessageKey> = {
  pending: 'status.pending',
  running: 'status.running',
  completed: 'status.completed',
  failed: 'status.failed'
}

const statusMap: Record<AutomationTaskStatus, Omit<StatusConfig, 'label'>> = {
  pending: { color: 'var(--text-tertiary)', icon: <Clock size={14} strokeWidth={1.5} aria-hidden /> },
  running: { color: 'var(--accent)', icon: <SpinnerIcon /> },
  completed: { color: 'var(--success)', icon: <CircleCheck size={14} strokeWidth={1.5} aria-hidden /> },
  failed: { color: 'var(--error)', icon: <TriangleAlert size={14} strokeWidth={1.5} aria-hidden /> }
}

export function StatusBadge({ status }: { status: AutomationTaskStatus }): JSX.Element {
  const t = useT()
  const cfg = statusMap[status] ?? statusMap.pending
  const labelKey = STATUS_LABEL_KEY[status] ?? STATUS_LABEL_KEY.pending
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        color: cfg.color,
        fontSize: '12px',
        fontWeight: 500
      }}
    >
      {cfg.icon}
      {t(labelKey)}
    </span>
  )
}

export function getStatusColor(status: AutomationTaskStatus): string {
  return (statusMap[status] ?? statusMap.pending).color
}
