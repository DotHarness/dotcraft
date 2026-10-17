import type { JSX, MouseEvent, ReactNode } from 'react'
import { ArrowUpRight } from 'lucide-react'

export function SidebarEntryDetailsActionRow({
  icon,
  label,
  title,
  ariaLabel,
  affordance = false,
  onClick
}: {
  icon: ReactNode
  label: string
  title?: string
  ariaLabel: string
  affordance?: boolean
  onClick: (event: MouseEvent<HTMLButtonElement>) => void
}): JSX.Element {
  return (
    <button
      type="button"
      className="sidebar-entry-details-row sidebar-entry-details-action-row"
      aria-label={ariaLabel}
      title={title}
      onClick={onClick}
    >
      {icon}
      <span>{label}</span>
      {affordance && (
        <ArrowUpRight
          className="sidebar-entry-details-action-row__affordance"
          size={14}
          aria-hidden
        />
      )}
    </button>
  )
}
