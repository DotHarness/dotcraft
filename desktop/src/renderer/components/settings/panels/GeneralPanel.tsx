import type { JSX, ReactNode } from 'react'

interface GeneralPanelProps {
  children: ReactNode
}

export function GeneralPanel({ children }: GeneralPanelProps): JSX.Element {
  return <>{children}</>
}
