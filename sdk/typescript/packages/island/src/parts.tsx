import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import type { AvatarPose } from '@dotcraft/avatar'
import { Avatar } from '@dotcraft/avatar/react'

export interface IslandRobot {
  id: string
  name: string
  pose: AvatarPose
  lead?: boolean
}

export function IslandRobots({ robots, size = 30, animate = true }: { robots: IslandRobot[]; size?: number; animate?: boolean }) {
  const ordered = [...robots].sort((a, b) => Number(Boolean(b.lead)) - Number(Boolean(a.lead)))
  return (
    <span className="dci-robots" aria-hidden="true" style={{ ['--dci-robot' as string]: `${size}px` }}>
      {ordered.map((robot) => (
        <span className="dci-robot" key={robot.id} data-lead={robot.lead || undefined}>
          <Avatar name={robot.name} size={size} state={robot.pose} motion={animate ? 'on' : 'off'} />
        </span>
      ))}
    </span>
  )
}

export function IslandDetail({ children, title, lead }: { children: string; title?: string; lead?: string }) {
  return (
    <span className="dci-detail" title={title ?? children}>
      {lead && <b>{lead}</b>}
      <span>{children}</span>
    </span>
  )
}

export function IslandBadge({ children }: { children: ReactNode }) {
  return <span className="dci-badge">{children}</span>
}

export type IslandButtonVariant = 'icon' | 'primary' | 'secondary'

export function IslandButton({ variant = 'secondary', className, type = 'button', ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: IslandButtonVariant; ref?: Ref<HTMLButtonElement> }) {
  return <button {...rest} type={type} className={className ? `dci-button ${className}` : 'dci-button'} data-variant={variant} />
}

const RING = 2 * Math.PI * 8

export function IslandCountdown({ fraction, label }: { fraction: number; label: string }) {
  const left = Math.min(1, Math.max(0, fraction))
  return (
    <span className="dci-countdown" aria-hidden="true">
      <svg viewBox="0 0 20 20">
        <circle cx="10" cy="10" r="8" className="dci-countdown__track" />
        <circle cx="10" cy="10" r="8" className="dci-countdown__value" style={{ strokeDasharray: RING, strokeDashoffset: RING * (1 - left) }} />
      </svg>
      <em>{label}</em>
    </span>
  )
}
