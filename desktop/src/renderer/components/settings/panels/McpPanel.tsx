import type { JSX, ReactNode } from 'react'

interface McpPanelProps {
  children: ReactNode
}

export function McpPanel({ children }: McpPanelProps): JSX.Element {
  return <>{children}</>
}
