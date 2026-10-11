import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronLeft, ChevronRight, Copy, SlidersHorizontal, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { IconButton } from '../ui/IconButton'
import { stepGalleryIndex } from './imagePreview/imageZoom'
import type { VisualizationEntry } from './visualizationGallery'
import { copyVisualizationImage } from './visualizationHost'
import { visualizationFileName } from './visualizationParser'

export type VisualizationViewerKey = 'Escape' | 'ArrowLeft' | 'ArrowRight'

export interface VisualizationFullscreenHost {
  maxHeight: number
  onExit: () => void
  onKey: (key: VisualizationViewerKey) => void
  onReadyChange: (ready: boolean) => void
  designControlsOpen: boolean
  capturing: boolean
  onDesignControlsAvailableChange: (available: boolean) => void
  onDesignControlsOpenChange: (open: boolean) => void
}

interface Props {
  entries: VisualizationEntry[]
  initialIndex: number
  onClose: () => void
  renderView: (entry: VisualizationEntry, host: VisualizationFullscreenHost) => ReactNode
}

const PANEL_PADDING = 16
const VIEWER_CHROME_HEIGHT = 128
const EDITABLE_TARGETS = 'input, textarea, select, [contenteditable="true"]'
const FOCUSABLE = 'button:not(:disabled), iframe, [tabindex]:not([tabindex="-1"])'

export function VisualizationViewer({ entries, initialIndex, onClose, renderView }: Props): JSX.Element {
  const t = useT()
  const titleId = useId()
  const rootRef = useRef<HTMLDivElement>(null)
  const stageRef = useRef<HTMLDivElement>(null)
  const backdropPressRef = useRef(false)
  const [index, setIndex] = useState(() => Math.min(Math.max(initialIndex, 0), entries.length - 1))
  const [ready, setReady] = useState(false)
  const [copying, setCopying] = useState(false)
  const [designAvailable, setDesignAvailable] = useState(false)
  const [designOpen, setDesignOpen] = useState(false)
  const [maxHeight, setMaxHeight] = useState(() => window.innerHeight - VIEWER_CHROME_HEIGHT - PANEL_PADDING * 2)
  const entry = entries[index]
  const title = entry.title ?? visualizationFileName(entry.path)
  const hasPrevious = stepGalleryIndex(index, -1, entries.length) != null
  const hasNext = stepGalleryIndex(index, 1, entries.length) != null

  const goTo = useCallback((delta: 1 | -1): void => {
    const next = stepGalleryIndex(index, delta, entries.length)
    if (next == null) return
    setReady(false)
    setDesignAvailable(false)
    setDesignOpen(false)
    setIndex(next)
  }, [entries.length, index])

  const handleKey = useCallback((key: VisualizationViewerKey): void => {
    if (key === 'Escape') onClose()
    else goTo(key === 'ArrowLeft' ? -1 : 1)
  }, [goTo, onClose])

  useLayoutEffect(() => {
    const stage = stageRef.current
    if (!stage) return
    const measure = (): void => { if (stage.clientHeight > 0) setMaxHeight(stage.clientHeight - PANEL_PADDING * 2) }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      const target = event.target instanceof Element ? event.target : null
      if (event.defaultPrevented || !(target === document.body || (target && rootRef.current?.contains(target)))) return
      if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.isComposing) return
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      } else if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && !target?.closest(`${EDITABLE_TARGETS}, [role="toolbar"]`)) {
        event.preventDefault()
        goTo(event.key === 'ArrowLeft' ? -1 : 1)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [goTo, onClose])

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    rootRef.current?.focus({ preventScroll: true })
    return () => { previous?.focus({ preventScroll: true }) }
  }, [])

  const focusEdge = (edge: 'first' | 'last'): void => {
    const focusable = [...(rootRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])].filter(element => !element.hasAttribute('data-focus-guard'))
    ;(edge === 'first' ? focusable[0] : focusable[focusable.length - 1])?.focus()
  }

  const isBackdrop = (event: ReactMouseEvent): boolean =>
    event.target instanceof Element && event.target.hasAttribute('data-viewer-backdrop')

  const copy = async (): Promise<void> => {
    if (copying || !ready) return
    setCopying(true)
    await copyVisualizationImage(() => stageRef.current?.querySelector('iframe') ?? null, t)
    setCopying(false)
  }

  const viewer = (
    <div
      ref={rootRef}
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      tabIndex={-1}
      className="visualization-viewer"
      data-viewer-backdrop
      onContextMenu={event => event.stopPropagation()}
      onMouseDown={event => { backdropPressRef.current = event.button === 0 && isBackdrop(event) }}
      onClick={event => {
        const pressed = backdropPressRef.current
        backdropPressRef.current = false
        if (pressed && isBackdrop(event)) onClose()
      }}
    >
      <div tabIndex={0} data-focus-guard onFocus={() => focusEdge('last')} />
      <div className="visualization-viewer__toolbar-row" data-viewer-backdrop>
        <div role="toolbar" aria-labelledby={titleId} className="visualization-viewer__toolbar">
          <span id={titleId} className="visualization-viewer__title">{title}</span>
          {designAvailable && (
            <IconButton
              size={34}
              radius={17}
              label={t('visualization.designControls')}
              tooltipLabel={t('visualization.designControls')}
              tooltipPlacement="bottom"
              active={designOpen}
              activeTone="neutral"
              aria-expanded={designOpen}
              onClick={() => setDesignOpen(open => !open)}
              icon={<SlidersHorizontal size={18} strokeWidth={2} aria-hidden />}
            />
          )}
          <IconButton
            size={34}
            radius={17}
            label={t('visualization.copyImage')}
            tooltipLabel={t('visualization.copyImage')}
            tooltipPlacement="bottom"
            disabled={!ready || copying}
            onClick={() => { void copy() }}
            icon={<Copy size={18} strokeWidth={2} aria-hidden />}
          />
          <IconButton
            size={34}
            radius={17}
            label={t('visualization.exitFullscreen')}
            tooltipLabel={t('visualization.exitFullscreen')}
            tooltipPlacement="bottom"
            onClick={onClose}
            icon={<X size={18} strokeWidth={2} aria-hidden />}
          />
        </div>
      </div>
      <div className="visualization-viewer__body" data-viewer-backdrop>
        <div className="visualization-viewer__gutter" data-viewer-backdrop>
          {hasPrevious && (
            <IconButton
              size={40}
              radius={20}
              bordered
              className="visualization-viewer__nav"
              label={t('visualization.previous')}
              tooltipLabel={t('visualization.previous')}
              onClick={() => goTo(-1)}
              icon={<ChevronLeft size={20} strokeWidth={2} aria-hidden />}
            />
          )}
        </div>
        <div ref={stageRef} className="visualization-viewer__stage" data-viewer-backdrop>
          <div className="visualization-viewer__panel">
            {renderView(entry, {
              maxHeight,
              onExit: onClose,
              onKey: handleKey,
              onReadyChange: setReady,
              designControlsOpen: designOpen,
              capturing: copying,
              onDesignControlsAvailableChange: setDesignAvailable,
              onDesignControlsOpenChange: setDesignOpen
            })}
          </div>
        </div>
        <div className="visualization-viewer__gutter" data-viewer-backdrop>
          {hasNext && (
            <IconButton
              size={40}
              radius={20}
              bordered
              className="visualization-viewer__nav"
              label={t('visualization.next')}
              tooltipLabel={t('visualization.next')}
              onClick={() => goTo(1)}
              icon={<ChevronRight size={20} strokeWidth={2} aria-hidden />}
            />
          )}
        </div>
      </div>
      <div className="visualization-viewer__footer" data-viewer-backdrop>
        {entries.length > 1 && (
          <span role="status" className="visualization-viewer__counter">
            {t('visualization.position', { current: index + 1, total: entries.length })}
          </span>
        )}
      </div>
      <div tabIndex={0} data-focus-guard onFocus={() => focusEdge('first')} />
    </div>
  )

  return createPortal(<LayerBoundary blocksNativeViews>{viewer}</LayerBoundary>, document.body)
}
