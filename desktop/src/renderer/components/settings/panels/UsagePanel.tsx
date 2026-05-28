import type { JSX, ReactNode } from 'react'

interface UsagePanelProps {
  children: ReactNode
}

export function UsagePanel({ children }: UsagePanelProps): JSX.Element {
  return <>{children}</>
}
