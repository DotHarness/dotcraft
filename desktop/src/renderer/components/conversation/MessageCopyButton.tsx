import type { CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { CopyButton } from '../ui/CopyButton'

interface MessageCopyButtonProps {
  getText: () => string
  visible: boolean
  disabled?: boolean
  wrapperStyle?: CSSProperties
}

export function MessageCopyButton({
  getText,
  visible,
  disabled = false,
  wrapperStyle
}: MessageCopyButtonProps): JSX.Element | null {
  const t = useT()
  if (disabled) return null

  return (
    <CopyButton
      getText={getText}
      label={t('conversation.copyMessage')}
      copiedLabel={t('common.copied')}
      tooltipWrapperStyle={{
        position: 'absolute',
        right: '8px',
        bottom: '6px',
        opacity: visible ? 1 : 0,
        pointerEvents: visible ? 'auto' : 'none',
        zIndex: 2,
        ...wrapperStyle
      }}
    />
  )
}
