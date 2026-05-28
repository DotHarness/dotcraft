interface CancelledNoticeProps {
  reason?: string
}

/**
 * Subtle notice shown when a turn was cancelled.
 * Spec §10.3.3 / §18.2
 */
export function CancelledNotice({ reason }: CancelledNoticeProps): JSX.Element {
  return (
    <div
      style={{
        color: 'var(--text-dimmed)',
        fontSize: '12px',
        fontStyle: 'italic',
        padding: '4px 0',
        marginTop: '4px'
      }}
    >
      {reason ? `Turn cancelled: ${reason}` : 'Turn cancelled.'}
    </div>
  )
}
