import { useCallback, useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X, ZoomIn, ZoomOut } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { IconButton } from '../ui/IconButton'

interface ImageLightboxProps {
  src: string
  alt?: string
  onClose: () => void
}

const MIN_SCALE = 0.5
const MAX_SCALE = 3
const ZOOM_STEP = 0.25
const LIGHTBOX_Z_INDEX = 35000

/**
 * Fullscreen image viewer: dark backdrop, centered image, Esc or backdrop to close.
 */
export function ImageLightbox({ src, alt = '', onClose }: ImageLightboxProps): JSX.Element {
  const t = useT()
  const [scale, setScale] = useState(1)
  const zoomLabel = `${Math.round(scale * 100)}%`

  const zoomIn = useCallback((): void => {
    setScale((current) => clampScale(current + ZOOM_STEP))
  }, [])

  const zoomOut = useCallback((): void => {
    setScale((current) => clampScale(current - ZOOM_STEP))
  }, [])

  const onKeyDown = useCallback(
    (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        onClose()
        return
      }
      if (e.key === '+' || e.key === '=') {
        e.preventDefault()
        zoomIn()
        return
      }
      if (e.key === '-' || e.key === '_') {
        e.preventDefault()
        zoomOut()
      }
    },
    [onClose, zoomIn, zoomOut]
  )

  useEffect(() => {
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [onKeyDown])

  const lightbox = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Image preview"
      onClick={onClose}
      style={overlayStyle}
    >
      <IconButton
          size={40}
          bordered
          label={t('common.close')}
          tooltipLabel={t('common.close')}
          tooltipPlacement="bottom"
          tooltipWrapperStyle={closeButtonWrapperStyle}
          onClick={(event) => {
            event.stopPropagation()
            onClose()
          }}
          style={closeButtonStyle}
          icon={<X size={20} strokeWidth={2} aria-hidden />}
      />

      <div style={imageViewportStyle}>
        <img
          src={src}
          alt={alt}
          onClick={(e) => {
            e.stopPropagation()
          }}
          draggable={false}
          style={{
            ...imageStyle,
            transform: `scale(${scale})`
          }}
        />
      </div>

      <div
        style={zoomControlsStyle}
        onClick={(event) => {
          event.stopPropagation()
        }}
      >
        <ZoomControlButton
          label={t('viewer.zoomOut')}
          onClick={zoomOut}
          disabled={scale <= MIN_SCALE}
        >
          <ZoomOut size={18} strokeWidth={2} aria-hidden />
        </ZoomControlButton>
        <span style={zoomLabelStyle}>{zoomLabel}</span>
        <ZoomControlButton
          label={t('viewer.zoomIn')}
          onClick={zoomIn}
          disabled={scale >= MAX_SCALE}
        >
          <ZoomIn size={18} strokeWidth={2} aria-hidden />
        </ZoomControlButton>
      </div>
    </div>
  )

  return createPortal(
    <LayerBoundary blocksNativeViews>{lightbox}</LayerBoundary>,
    document.body
  )
}

function clampScale(value: number): number {
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, Number(value.toFixed(2))))
}

function ZoomControlButton({
  label,
  onClick,
  disabled,
  children
}: {
  label: string
  onClick: () => void
  disabled: boolean
  children: ReactNode
}): JSX.Element {
  return (
      <IconButton
        size={34}
        label={label}
        tooltipLabel={label}
        tooltipPlacement="top"
        onClick={onClick}
        disabled={disabled}
        style={{ borderRadius: '50%' }}
        icon={children}
      />
  )
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: LIGHTBOX_Z_INDEX,
  background: 'rgba(0,0,0,0.88)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px',
  cursor: 'zoom-out'
}

const imageViewportStyle: CSSProperties = {
  position: 'absolute',
  inset: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '56px 24px 92px',
  overflow: 'hidden'
}

const imageStyle: CSSProperties = {
  maxWidth: '100%',
  maxHeight: '100%',
  objectFit: 'contain',
  borderRadius: '8px',
  cursor: 'default',
  transformOrigin: 'center center',
  transition: 'transform 140ms ease',
  pointerEvents: 'auto'
}

const closeButtonStyle: CSSProperties = {
  borderRadius: '50%',
  color: 'var(--text-primary)',
  boxShadow: 'var(--shadow-overlay)'
}

const closeButtonWrapperStyle: CSSProperties = {
  position: 'absolute',
  top: 14,
  right: 14,
  zIndex: 2
}

const zoomControlsStyle: CSSProperties = {
  position: 'absolute',
  left: '50%',
  bottom: 24,
  transform: 'translateX(-50%)',
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  minHeight: 42,
  padding: '4px',
  borderRadius: 999,
  border: '1px solid var(--glass-border)',
  background: 'var(--bg-elevated)',
  color: 'var(--text-primary)',
  boxShadow: 'var(--shadow-overlay)',
  zIndex: 2,
  cursor: 'default'
}

const zoomLabelStyle: CSSProperties = {
  minWidth: 52,
  textAlign: 'center',
  fontSize: '12px',
  lineHeight: 1,
  color: 'var(--text-primary)',
  fontVariantNumeric: 'tabular-nums'
}
