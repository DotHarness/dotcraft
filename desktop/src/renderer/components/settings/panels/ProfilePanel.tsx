import type { JSX, ReactNode } from 'react'

interface ProfilePanelProps {
  children: ReactNode
}

export function ProfilePanel({ children }: ProfilePanelProps): JSX.Element {
  return <>{children}</>
}
