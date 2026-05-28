import type { CSSProperties, JSX, ReactNode } from 'react'

interface ConversationColumnProps {
  children: ReactNode
  style?: CSSProperties
}

export function ConversationColumn({ children, style }: ConversationColumnProps): JSX.Element {
  return (
    <div style={{ ...conversationColumnStyle(), ...style }}>
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
