import type { CSSProperties, JSX, ReactNode } from 'react'

interface ConversationColumnProps {
  children: ReactNode
  className?: string
  style?: CSSProperties
}

export function ConversationColumn({ children, className, style }: ConversationColumnProps): JSX.Element {
  return (
    <div className={className} style={{ ...conversationColumnStyle(), ...style }}>
      {children}
    </div>
  )
}

export function conversationColumnStyle(): CSSProperties {
  return {
    width: '100%',
    maxWidth: 'var(--conversation-reading-width)',
    margin: '0 auto',
    boxSizing: 'border-box'
  }
}
