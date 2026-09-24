import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { CopyButton } from '../ui/CopyButton'

interface ErrorBlockProps {
  message: string
}

/** Error block for turn/failed or error items. Spec §10.3.3 / §18.3. */
export function ErrorBlock({ message }: ErrorBlockProps): JSX.Element {
  const t = useT()

  return (
    <div
      role="alert"
      style={{
        backgroundColor: 'rgba(239, 68, 68, 0.1)',
        border: '1px solid var(--error)',
        borderRadius: '6px',
        padding: '10px 46px 10px 14px',
        color: 'var(--error)',
        fontSize: 'var(--conversation-secondary-size)',
        lineHeight: 'var(--conversation-line-height)',
        marginTop: '4px',
        position: 'relative'
      }}
    >
      <CopyButton
        key={message}
        getText={() => message}
        label={t('error.copyAria')}
        copiedLabel={t('error.copiedAria')}
        tooltipWrapperStyle={copyButtonWrapperStyle}
      />
      <strong style={{ display: 'block', marginBottom: '4px', fontWeight: 600 }}>Error</strong>
      <span>{message}</span>
    </div>
  )
}

const copyButtonWrapperStyle: CSSProperties = {
  position: 'absolute',
  top: '8px',
  right: '8px'
}
