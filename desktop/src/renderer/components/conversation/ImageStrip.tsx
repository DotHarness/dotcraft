import type { ImageAttachment } from '../../types/conversation'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ImageStripProps {
  images: ImageAttachment[]
  onRemove: (index: number) => void
}

/**
 * Horizontal scrollable row of image attachment pills above the composer input.
 */
export function ImageStrip({ images, onRemove }: ImageStripProps): JSX.Element | null {
  if (images.length === 0) return null

  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'nowrap',
        gap: '8px',
        overflowX: 'auto',
        paddingBottom: '4px',
        alignItems: 'flex-start'
      }}
    >
      {images.map((img, idx) => (
        <div
          key={`${img.tempPath}-${idx}`}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '6px',
            flexShrink: 0,
            maxWidth: '200px',
            padding: '4px 8px',
            borderRadius: '8px',
            background: 'var(--bg-tertiary)',
            border: '1px solid var(--border-default)',
            fontSize: '12px',
            color: 'var(--text-secondary)'
          }}
        >
          <img
            src={img.dataUrl}
            alt=""
            style={{
              width: 20,
              height: 20,
              borderRadius: '3px',
              objectFit: 'cover',
              flexShrink: 0
            }}
          />
          <span
            style={{
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              maxWidth: '140px'
            }}
            title={img.fileName}
          >
            {img.fileName}
          </span>
          <ActionTooltip label="Remove" placement="top">
          <button
            type="button"
            onClick={() => { onRemove(idx) }}
            aria-label="Remove image"
            style={{
              width: 16,
              height: 16,
              borderRadius: '50%',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-secondary)',
              color: 'var(--text-secondary)',
              fontSize: '10px',
              cursor: 'pointer',
              padding: 0,
              lineHeight: 1,
              flexShrink: 0
            }}
          >
            ✕
          </button>
          </ActionTooltip>
        </div>
      ))}
    </div>
  )
}
