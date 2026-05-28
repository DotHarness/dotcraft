import type { JSX, ReactNode } from 'react'

interface ConnectionPanelProps {
  children: ReactNode
}

export function ConnectionPanel({ children }: ConnectionPanelProps): JSX.Element {
  return <>{children}</>
}
