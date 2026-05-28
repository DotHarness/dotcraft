/**
 * Image viewer with zoom-to-fit and 100% toggle.
 * Uses a `dotcraft-viewer://` URL for the image src, which is served securely
 * from the main process with workspace boundary enforcement.
 *
 * References: orca/src/renderer/src/components/editor/ImageViewer.tsx
 */
import { useState, useRef, useCallback } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { buildViewerUrlRenderer } from '../../../utils/viewerUrl'
import { ZoomIn, ZoomOut, Maximize2 } from 'lucide-react'
import { ActionTooltip } from '../../ui/ActionTooltip'

interface ImageViewerProps {
  absolutePath: string
  sizeBytes?: number
}

export function ImageViewer({ absolutePath, sizeBytes }: ImageViewerProps): JSX.Element {
  const t = useT()
  const [scale, setScale] = useState(1)
  const [fitMode, setFitMode] = useState(true)
  const [loadError, setLoadError] = useState(false)
  const [dimensions, setDimensions] = useState<{ w: number; h: number } | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const imgRef = useRef<HTMLImageElement>(null)

  const src = buildViewerUrlRenderer(absolutePath)

  const handleZoomIn = useCallback(() => {
    setFitMode(false)
    setScale((s) => Math.min(s * 1.25, 8))
  }, [])

  const handleZoomOut = useCallback(() => {
    setFitMode(false)
    setScale((s) => Math.max(s / 1.25, 0.1))
  }, [])

  const handleFit = useCallback(() => {
    setFitMode(true)
    setScale(1)
  }, [])

  const handle100 = useCallback(() => {
    setFitMode(false)
    setScale(1)
  }, [])

  if (loadError) {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: '13px'
      }}>
        {t('viewer.readFailed')}
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      {/* Toolbar */}
      <div style={{
        display: 'flex',
        gap: '4px',
        padding: '4px 8px',
        borderBottom: '1px solid var(--border-default)',
        flexShrink: 0,
        alignItems: 'center'
      }}>
        <ToolbarButton onClick={handleZoomOut} title={t('viewer.zoomOut')}>
          <ZoomOut size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
        <span style={{ fontSize: '12px', color: 'var(--text-secondary)', minWidth: '40px', textAlign: 'center' }}>
          {fitMode ? 'Fit' : `${Math.round(scale * 100)}%`}
        </span>
        <ToolbarButton onClick={handleZoomIn} title={t('viewer.zoomIn')}>
          <ZoomIn size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
        <ToolbarButton onClick={handleFit} title={t('viewer.zoomFit')}>
          <Maximize2 size={14} aria-hidden style={{ display: 'block' }} />
        </ToolbarButton>
        <ToolbarButton onClick={handle100} title="100%">
          <span style={{ fontSize: '11px', fontWeight: 500 }}>1:1</span>
        </ToolbarButton>
        {/* Image info: pixel dimensions and byte size */}
        <span
          aria-label={t('viewer.imageInfo')}
          style={{
            marginLeft: 'auto',
            fontSize: '11px',
            color: 'var(--text-tertiary, var(--text-secondary))',
            whiteSpace: 'nowrap',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            maxWidth: '180px'
          }}
        >
          {dimensions && `${dimensions.w} × ${dimensions.h}px`}
          {dimensions && sizeBytes !== undefined && '  ·  '}
          {sizeBytes !== undefined && formatBytes(sizeBytes)}
        </span>
      </div>

      {/* Image container */}
      <div
        ref={containerRef}
        style={{
          flex: 1,
          overflow: fitMode ? 'hidden' : 'auto',
          display: 'flex',
          alignItems: fitMode ? 'center' : 'flex-start',
          justifyContent: fitMode ? 'center' : 'flex-start',
          padding: fitMode ? '8px' : '16px'
        }}
      >
        <img
          ref={imgRef}
          src={src}
          alt={absolutePath.replace(/\\/g, '/').split('/').pop()}
          onLoad={(e) => {
            const img = e.currentTarget
            setDimensions({ w: img.naturalWidth, h: img.naturalHeight })
          }}
          onError={() => setLoadError(true)}
          draggable={false}
          style={{
            display: 'block',
            maxWidth: fitMode ? '100%' : 'none',
            maxHeight: fitMode ? '100%' : 'none',
            width: fitMode ? 'auto' : `${scale * 100}%`,
            height: fitMode ? 'auto' : 'auto',
            transform: 'none',
            transformOrigin: 'top left',
            imageRendering: scale > 2 ? 'pixelated' : 'auto'
          }}
        />
      </div>
    </div>
  )
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function ToolbarButton({
  onClick,
  title,
  children
}: {
  onClick: () => void
  title: string
  children: React.ReactNode
}): JSX.Element {
  return (
    <ActionTooltip label={title} placement="bottom">
      <button
        onClick={onClick}
        aria-label={title}
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: '24px',
          height: '24px',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-secondary)',
          cursor: 'pointer',
          borderRadius: '3px',
          padding: 0
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--bg-hover, rgba(255,255,255,0.06))'
        }}
        onMouseLeave={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        {children}
      </button>
    </ActionTooltip>
  )
}
