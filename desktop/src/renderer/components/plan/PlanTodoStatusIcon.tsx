import type { CSSProperties } from 'react'
import { Circle, CircleCheck, CircleDot, CircleX, type LucideIcon } from 'lucide-react'

export type PlanTodoStatusIconStatus = 'pending' | 'in_progress' | 'completed' | 'cancelled'

export const PLAN_TODO_STATUS_ICON_NAMES: Record<PlanTodoStatusIconStatus, string> = {
  pending: 'circle',
  in_progress: 'circle-dot',
  completed: 'circle-check',
  cancelled: 'circle-x'
}

const STATUS_ICONS: Record<PlanTodoStatusIconStatus, LucideIcon> = {
  pending: Circle,
  in_progress: CircleDot,
  completed: CircleCheck,
  cancelled: CircleX
}

const STATUS_COLORS: Record<PlanTodoStatusIconStatus, string> = {
  pending: 'var(--text-dimmed)',
  in_progress: 'var(--text-secondary)',
  completed: 'var(--text-secondary)',
  cancelled: 'var(--text-dimmed)'
}

interface PlanTodoStatusIconProps {
  status: PlanTodoStatusIconStatus
  size?: number
  slotSize?: number
  strokeWidth?: number
  style?: CSSProperties
}

export function PlanTodoStatusIcon({
  status,
  size = 15,
  slotSize = 16,
  strokeWidth = 1.8,
  style
}: PlanTodoStatusIconProps): JSX.Element {
  const Icon = STATUS_ICONS[status] ?? Circle
  return (
    <span
      aria-hidden="true"
      data-plan-todo-status={status}
      data-plan-todo-icon={PLAN_TODO_STATUS_ICON_NAMES[status]}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        width: `${slotSize}px`,
        height: `${slotSize}px`,
        lineHeight: 0,
        color: STATUS_COLORS[status] ?? STATUS_COLORS.pending,
        ...style
      }}
    >
      <Icon size={size} strokeWidth={strokeWidth} aria-hidden="true" />
    </span>
  )
}
