import type { CSSProperties, JSX, ReactNode, Ref } from 'react'

interface ConversationColumnProps {
  children: ReactNode
  className?: string
  style?: CSSProperties
  ref?: Ref<HTMLDivElement>
}

export function ConversationColumn({ children, className, style, ref }: ConversationColumnProps): JSX.Element {
  return (
    <div
      ref={ref}
      className={className ? `dc-conversation-column ${className}` : 'dc-conversation-column'}
      style={{ ...conversationColumnStyle(), ...style }}
    >
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
