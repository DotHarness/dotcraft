import type { ReactNode } from 'react'

import './oratorio-space-icons.css'

function SpaceIcon({ name, children }: { name: string; children: ReactNode }): JSX.Element {
  return (
    <svg
      className={`ora-space-icon ora-space-icon--${name}`}
      width={16}
      height={16}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

export function ConductorIcon(): JSX.Element {
  return (
    <SpaceIcon name="conductor">
      <path className="ora-space-icon__cue" d="M14 9a2 2 0 0 1-2 2H6l-4 4V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2z" />
      <path className="ora-space-icon__answer" d="M18 9h2a2 2 0 0 1 2 2v11l-4-4h-6a2 2 0 0 1-2-2v-1" />
    </SpaceIcon>
  )
}

export function BoardIcon(): JSX.Element {
  return (
    <SpaceIcon name="board">
      <rect width="18" height="18" x="3" y="3" rx="2" />
      <path className="ora-space-icon__column" d="M8 7v7" />
      <path className="ora-space-icon__column" d="M12 7v4" />
      <path className="ora-space-icon__column" d="M16 7v9" />
    </SpaceIcon>
  )
}
