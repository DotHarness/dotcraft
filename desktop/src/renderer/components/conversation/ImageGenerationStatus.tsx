import type { CSSProperties } from 'react'
import { Image as ImageIcon } from 'lucide-react'
import type { ConversationItem } from '../../types/conversation'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { ErrorBlock } from './ErrorBlock'
import { Skeleton } from '../ui/Skeleton'

export function ImageGenerationStatus({ item }: { item: ConversationItem }): JSX.Element {
  const locale = useLocale()
  const status = item.imageGenerationStatus ?? (item.status === 'completed' ? 'completed' : 'inProgress')
  const image = status === 'completed' && Boolean(item.result?.trim())
  const isInProgress = status === 'inProgress'

  if (status === 'failed') {
    return (
      <ErrorBlock
        message={item.errorCode === 'image_generation_result_missing'
          ? translate(locale, 'conversation.imageGeneration.failed')
          : item.errorMessage?.trim() || translate(locale, 'conversation.imageGeneration.failed')}
      />
    )
  }

  if (status === 'completed' && !image) {
    return <ErrorBlock message={translate(locale, 'conversation.imageGeneration.noImageData')} />
  }

  const label = status === 'completed'
    ? translate(locale, 'conversation.imageGeneration.completed')
    : translate(locale, 'conversation.imageGeneration.generating')

  return (
    <>
      {item.saveStatus === 'failed' && <ErrorBlock message={translate(locale, 'conversation.imageGeneration.saveFailed')} />}
      <div
        role={isInProgress ? 'status' : undefined}
        aria-live={isInProgress ? 'polite' : undefined}
        aria-busy={isInProgress ? true : undefined}
        aria-label={isInProgress ? label : undefined}
        style={isInProgress ? imageGenerationProgressStyle : undefined}
      >
        <div
          data-testid="image-generation-row"
          style={imageGenerationRowStyle}
        >
          <ImageIcon size={15} strokeWidth={1.8} aria-hidden="true" style={imageGenerationIconStyle} />
          <span
            className={isInProgress ? 'tool-running-gradient-text' : undefined}
            style={imageGenerationLabelStyle}
          >
            {label}
          </span>
        </div>
        {isInProgress && (
          <div data-testid="image-generation-skeleton" style={imageGenerationSkeletonFrameStyle}>
            <Skeleton width="100%" height="100%" radius={4} />
          </div>
        )}
      </div>
    </>
  )
}

const imageGenerationProgressStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '6px'
}

const imageGenerationRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minHeight: '28px',
  padding: '3px 6px',
  color: 'var(--text-secondary)',
  fontSize: '12px',
  lineHeight: 1.35,
  userSelect: 'none'
}

const imageGenerationIconStyle: CSSProperties = {
  flex: '0 0 auto',
  color: 'var(--text-dimmed)'
}

const imageGenerationLabelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontWeight: 600
}

const imageGenerationSkeletonFrameStyle: CSSProperties = {
  width: '180px',
  height: '180px',
  padding: '0 6px'
}
