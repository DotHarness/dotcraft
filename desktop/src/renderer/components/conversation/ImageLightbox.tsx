import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type MouseEvent as ReactMouseEvent,
  type PointerEvent as ReactPointerEvent
} from 'react'
import { createPortal, flushSync } from 'react-dom'
import { ChevronDown, ChevronLeft, ChevronRight, Download, X } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { IconButton } from '../ui/IconButton'
import { Button } from '../ui/Button'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'
import { resolveGalleryImageSrc, type GalleryImage } from './imagePreview/galleryImages'
import { downloadImage, imageContextMenuEntries } from './imagePreview/imageActions'
import {
  IMAGE_ZOOM_PRESETS,
  movedBeyondClickSlop,
  pinchImageZoom,
  stepGalleryIndex,
  stepImageZoom,
  wheelImageZoom
} from './imagePreview/imageZoom'

interface ImageLightboxProps {
  images: GalleryImage[]
  initialIndex: number
  onClose: () => void
}

interface ClientPoint {
  clientX: number
  clientY: number
}

const LIGHTBOX_Z_INDEX = 35000
const LIGHTBOX_MENU_Z_INDEX = LIGHTBOX_Z_INDEX + 10
const NON_BACKDROP_TARGETS =
  'button, a, input, textarea, select, [role="toolbar"], [role="menu"], [role="menuitem"]'

export function ImageLightbox({ images: gallery, initialIndex, onClose }: ImageLightboxProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [index, setIndex] = useState(() => Math.min(Math.max(initialIndex, 0), gallery.length - 1))
  const image = gallery[Math.min(index, gallery.length - 1)]
  const [resolved, setResolved] = useState<{ key: string; src: string } | null>(null)
  const [failedKey, setFailedKey] = useState<string | null>(null)
  const [natural, setNatural] = useState<{ width: number; height: number } | null>(null)
  const [zoom, setZoom] = useState<number | null>(null)
  const [fitPercent, setFitPercent] = useState(100)
  const [overflowing, setOverflowing] = useState(false)
  const [spaceHeld, setSpaceHeld] = useState(false)
  const [panning, setPanning] = useState(false)
  const [zoomMenu, setZoomMenu] = useState<ContextMenuPosition | null>(null)
  const [imageMenu, setImageMenu] = useState<ContextMenuPosition | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const imgRef = useRef<HTMLImageElement>(null)
  const panRef = useRef<{ pointerId: number; x: number; y: number; left: number; top: number } | null>(null)
  const pinchRef = useRef<{ distance: number; zoom: number } | null>(null)
  const backdropPressRef = useRef<{ x: number; y: number } | null>(null)

  const currentSrc = image.src ?? (resolved?.key === image.key ? resolved.src : undefined)
  const failed = failedKey === image.key
  const effectiveZoom = zoom ?? fitPercent
  const displayedZoom = Math.round(effectiveZoom)
  const title = image.title?.trim() || t('imagePreview.imageAlt', { index: index + 1 })
  const hasPrevious = stepGalleryIndex(index, -1, gallery.length) != null
  const hasNext = stepGalleryIndex(index, 1, gallery.length) != null
  const menuOpen = zoomMenu != null || imageMenu != null

  useEffect(() => {
    setNatural(null)
    if (image.src) return
    let cancelled = false
    resolveGalleryImageSrc(image)
      .then((value) => { if (!cancelled) setResolved({ key: image.key, src: value }) })
      .catch(() => { if (!cancelled) setFailedKey(image.key) })
    return () => { cancelled = true }
  }, [image])

  const measure = useCallback((): void => {
    const scroller = scrollRef.current
    const img = imgRef.current
    if (!scroller) return
    setOverflowing(scroller.scrollWidth > scroller.clientWidth + 1 || scroller.scrollHeight > scroller.clientHeight + 1)
    if (zoom == null && img && img.naturalWidth > 0 && img.clientWidth > 0) {
      setFitPercent((img.clientWidth / img.naturalWidth) * 100)
    }
  }, [zoom])

  useLayoutEffect(() => {
    measure()
  }, [measure, natural, currentSrc])

  useEffect(() => {
    const scroller = scrollRef.current
    if (!scroller || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() => measure())
    observer.observe(scroller)
    return () => observer.disconnect()
  }, [measure])

  const applyZoom = useCallback((next: number | null, anchor?: ClientPoint): void => {
    const scroller = scrollRef.current
    const img = imgRef.current
    if (!scroller || !img || !natural) {
      setZoom(next)
      return
    }
    const before = img.getBoundingClientRect()
    const box = scroller.getBoundingClientRect()
    const point = anchor ?? { clientX: box.left + box.width / 2, clientY: box.top + box.height / 2 }
    const fx = before.width > 0 ? clamp01((point.clientX - before.left) / before.width) : 0.5
    const fy = before.height > 0 ? clamp01((point.clientY - before.top) / before.height) : 0.5
    flushSync(() => setZoom(next))
    if (next == null) {
      scroller.scrollLeft = 0
      scroller.scrollTop = 0
      return
    }
    const after = img.getBoundingClientRect()
    scroller.scrollLeft += after.left + after.width * fx - point.clientX
    scroller.scrollTop += after.top + after.height * fy - point.clientY
  }, [natural])

  const zoomRef = useRef(effectiveZoom)
  zoomRef.current = effectiveZoom
  const applyZoomRef = useRef(applyZoom)
  applyZoomRef.current = applyZoom

  useEffect(() => {
    const scroller = scrollRef.current
    if (!scroller) return
    const onWheel = (event: WheelEvent): void => {
      if (!event.ctrlKey && !event.metaKey) return
      event.preventDefault()
      const next = wheelImageZoom(zoomRef.current, event.deltaY)
      if (next !== Math.round(zoomRef.current)) {
        applyZoomRef.current(next, { clientX: event.clientX, clientY: event.clientY })
      }
    }
    const onTouchStart = (event: TouchEvent): void => {
      if (event.touches.length !== 2) {
        pinchRef.current = null
        return
      }
      event.preventDefault()
      pinchRef.current = { distance: touchDistance(event.touches), zoom: zoomRef.current }
    }
    const onTouchMove = (event: TouchEvent): void => {
      const pinch = pinchRef.current
      if (event.touches.length !== 2 || !pinch) return
      event.preventDefault()
      const distance = touchDistance(event.touches)
      if (distance <= 0 || pinch.distance <= 0) return
      const next = pinchImageZoom(pinch.distance, pinch.zoom, distance)
      if (next === Math.round(zoomRef.current)) return
      const [a, b] = [event.touches[0], event.touches[1]]
      applyZoomRef.current(next, { clientX: (a.clientX + b.clientX) / 2, clientY: (a.clientY + b.clientY) / 2 })
    }
    const onTouchEnd = (event: TouchEvent): void => {
      if (event.touches.length < 2) pinchRef.current = null
    }
    scroller.addEventListener('wheel', onWheel, { passive: false })
    scroller.addEventListener('touchstart', onTouchStart, { passive: false })
    scroller.addEventListener('touchmove', onTouchMove, { passive: false })
    scroller.addEventListener('touchend', onTouchEnd)
    scroller.addEventListener('touchcancel', onTouchEnd)
    return () => {
      scroller.removeEventListener('wheel', onWheel)
      scroller.removeEventListener('touchstart', onTouchStart)
      scroller.removeEventListener('touchmove', onTouchMove)
      scroller.removeEventListener('touchend', onTouchEnd)
      scroller.removeEventListener('touchcancel', onTouchEnd)
    }
  }, [])

  const goTo = useCallback((delta: 1 | -1): void => {
    const next = stepGalleryIndex(index, delta, gallery.length)
    if (next == null) return
    setZoom(null)
    setIndex(next)
    const scroller = scrollRef.current
    if (scroller) {
      scroller.scrollLeft = 0
      scroller.scrollTop = 0
    }
  }, [gallery.length, index])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (menuOpen || event.defaultPrevented) return
      if (event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        onClose()
        return
      }
      const target = event.target instanceof Element ? event.target : null
      if (target?.closest('input, textarea, select, [contenteditable="true"]')) return
      if (event.code === 'Space') {
        if (target?.closest('button')) return
        event.preventDefault()
        setSpaceHeld(true)
        return
      }
      if (event.ctrlKey || event.metaKey || event.altKey || event.isComposing) return
      if (event.key === '+' || event.key === '=') {
        event.preventDefault()
        applyZoomRef.current(stepImageZoom(zoomRef.current, 1))
      } else if (event.key === '-' || event.key === '_') {
        event.preventDefault()
        applyZoomRef.current(stepImageZoom(zoomRef.current, -1))
      } else if (event.key === '0') {
        event.preventDefault()
        applyZoomRef.current(null)
      } else if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight') && !event.shiftKey) {
        if (target?.closest('[role="toolbar"]')) return
        event.preventDefault()
        event.stopPropagation()
        goTo(event.key === 'ArrowLeft' ? -1 : 1)
      }
    }
    function onKeyUp(event: KeyboardEvent): void {
      if (event.code === 'Space') setSpaceHeld(false)
    }
    function onBlur(): void {
      setSpaceHeld(false)
    }
    window.addEventListener('keydown', onKeyDown, true)
    window.addEventListener('keyup', onKeyUp, true)
    window.addEventListener('blur', onBlur)
    return () => {
      window.removeEventListener('keydown', onKeyDown, true)
      window.removeEventListener('keyup', onKeyUp, true)
      window.removeEventListener('blur', onBlur)
    }
  }, [goTo, menuOpen, onClose])

  useEffect(() => {
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    rootRef.current?.focus({ preventScroll: true })
    return () => { previous?.focus({ preventScroll: true }) }
  }, [])

  const isBackdropTarget = (event: ReactPointerEvent | ReactMouseEvent): boolean => {
    const target = event.target
    if (!(target instanceof Element) || !event.currentTarget.contains(target)) return false
    if (target.closest(NON_BACKDROP_TARGETS)) return false
    const rect = imgRef.current?.getBoundingClientRect()
    if (!rect || rect.width === 0 || rect.height === 0) return true
    return event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom
  }

  const endPan = (): void => {
    panRef.current = null
    setPanning(false)
  }

  const onScrollerPointerDown = (event: ReactPointerEvent<HTMLDivElement>): void => {
    const scroller = scrollRef.current
    if (!scroller || event.button !== 0 || event.pointerType === 'touch') return
    if (scroller.scrollWidth <= scroller.clientWidth && scroller.scrollHeight <= scroller.clientHeight) return
    event.preventDefault()
    panRef.current = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      left: scroller.scrollLeft,
      top: scroller.scrollTop
    }
    event.currentTarget.setPointerCapture?.(event.pointerId)
    setPanning(true)
  }

  const onScrollerPointerMove = (event: ReactPointerEvent<HTMLDivElement>): void => {
    const pan = panRef.current
    const scroller = scrollRef.current
    if (!pan || !scroller || pan.pointerId !== event.pointerId) return
    event.preventDefault()
    scroller.scrollLeft = pan.left - (event.clientX - pan.x)
    scroller.scrollTop = pan.top - (event.clientY - pan.y)
  }

  const openZoomMenu = (event: ReactMouseEvent<HTMLButtonElement>): void => {
    if (zoomMenu) {
      setZoomMenu(null)
      return
    }
    const rect = event.currentTarget.getBoundingClientRect()
    setZoomMenu({ x: rect.right - 200, y: rect.bottom + 4 })
  }

  const zoomMenuItems: ContextMenuEntry[] = [
    ...IMAGE_ZOOM_PRESETS.map((percent): ContextMenuEntry => ({
      label: t('imagePreview.zoomPercent', { percent }),
      selection: 'radio',
      checked: zoom === percent,
      onClick: () => applyZoom(percent)
    })),
    { type: 'separator' },
    {
      label: t('imagePreview.zoomToFit'),
      selection: 'radio',
      checked: zoom == null,
      onClick: () => applyZoom(null)
    }
  ]

  const canPan = overflowing || spaceHeld
  const lightbox = (
    <div
      ref={rootRef}
      role="dialog"
      aria-modal="true"
      aria-label={t('imagePreview.label')}
      tabIndex={-1}
      style={overlayStyle}
      onPointerDownCapture={(event) => {
        backdropPressRef.current = event.isPrimary && event.button === 0 && isBackdropTarget(event)
          ? { x: event.clientX, y: event.clientY }
          : null
      }}
      onPointerMoveCapture={(event) => {
        const press = backdropPressRef.current
        if (press && movedBeyondClickSlop(press, { x: event.clientX, y: event.clientY })) {
          backdropPressRef.current = null
        }
      }}
      onPointerCancelCapture={() => { backdropPressRef.current = null }}
      onClick={(event) => {
        const pressed = backdropPressRef.current != null
        backdropPressRef.current = null
        if (pressed && !event.defaultPrevented && isBackdropTarget(event)) onClose()
      }}
    >
      <div role="toolbar" aria-label={t('imagePreview.label')} style={toolbarRowStyle}>
        <div style={toolbarPillStyle}>
          <Button
            variant="ghost"
            size="toolbar"
            aria-label={t('imagePreview.currentZoom', { percent: displayedZoom })}
            aria-haspopup="menu"
            aria-expanded={zoomMenu != null}
            onMouseDown={(event) => event.stopPropagation()}
            onClick={openZoomMenu}
            style={zoomTriggerStyle}
          >
            <span>{t('imagePreview.zoomPercent', { percent: displayedZoom })}</span>
            <ChevronDown size={14} strokeWidth={2} aria-hidden />
          </Button>
          <IconButton
            size={34}
            radius={17}
            label={t('imagePreview.download')}
            tooltipLabel={t('imagePreview.download')}
            tooltipPlacement="bottom"
            onClick={() => { void downloadImage(image, t, locale) }}
            icon={<Download size={18} strokeWidth={2} aria-hidden />}
          />
          <IconButton
            size={34}
            radius={17}
            label={t('imagePreview.close')}
            tooltipLabel={t('imagePreview.close')}
            tooltipPlacement="bottom"
            onClick={onClose}
            icon={<X size={18} strokeWidth={2} aria-hidden />}
          />
        </div>
      </div>

      <div style={bodyStyle}>
        <div style={gutterStyle}>
          {hasPrevious && (
            <IconButton
              size={40}
              radius={20}
              bordered
              label={t('imagePreview.previous')}
              tooltipLabel={t('imagePreview.previous')}
              onClick={() => goTo(-1)}
              style={navButtonStyle}
              icon={<ChevronLeft size={20} strokeWidth={2} aria-hidden />}
            />
          )}
        </div>
        <div
          ref={scrollRef}
          style={{
            ...scrollerStyle,
            overflow: zoom == null ? 'hidden' : 'auto',
            cursor: canPan ? (panning ? 'grabbing' : 'grab') : 'default'
          }}
          onPointerDown={onScrollerPointerDown}
          onPointerMove={onScrollerPointerMove}
          onPointerUp={endPan}
          onPointerCancel={endPan}
          onLostPointerCapture={endPan}
        >
          <div style={zoom == null ? fitStageStyle : zoomStageStyle}>
            {failed ? (
              <div role="status" style={loadFailedStyle}>{t('imagePreview.loadFailed')}</div>
            ) : currentSrc ? (
              <img
                ref={imgRef}
                src={currentSrc}
                alt={title}
                draggable={false}
                onLoad={(event) => {
                  const img = event.currentTarget
                  setNatural({ width: img.naturalWidth, height: img.naturalHeight })
                }}
                onError={() => setFailedKey(image.key)}
                onContextMenu={(event) => {
                  event.preventDefault()
                  event.stopPropagation()
                  setImageMenu({ x: event.clientX, y: event.clientY })
                }}
                style={
                  zoom != null && natural
                    ? {
                        ...imageStyle,
                        width: (natural.width * zoom) / 100,
                        height: (natural.height * zoom) / 100,
                        maxWidth: 'none',
                        maxHeight: 'none'
                      }
                    : { ...imageStyle, maxWidth: '100%', maxHeight: '100%' }
                }
              />
            ) : null}
          </div>
        </div>
        <div style={gutterStyle}>
          {hasNext && (
            <IconButton
              size={40}
              radius={20}
              bordered
              label={t('imagePreview.next')}
              tooltipLabel={t('imagePreview.next')}
              onClick={() => goTo(1)}
              style={navButtonStyle}
              icon={<ChevronRight size={20} strokeWidth={2} aria-hidden />}
            />
          )}
        </div>
      </div>

      <div style={footerStyle}>
        {gallery.length > 1 && (
          <span role="status" style={counterStyle}>
            {t('imagePreview.position', { current: index + 1, total: gallery.length })}
          </span>
        )}
      </div>

      {zoomMenu && (
        <ContextMenu
          items={zoomMenuItems}
          position={zoomMenu}
          zIndex={LIGHTBOX_MENU_Z_INDEX}
          onClose={() => setZoomMenu(null)}
        />
      )}
      {imageMenu && (
        <ContextMenu
          items={imageContextMenuEntries(image, { t, locale })}
          position={imageMenu}
          zIndex={LIGHTBOX_MENU_Z_INDEX}
          onClose={() => setImageMenu(null)}
        />
      )}
    </div>
  )

  return createPortal(
    <LayerBoundary blocksNativeViews>{lightbox}</LayerBoundary>,
    document.body
  )
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value))
}

function touchDistance(touches: TouchList): number {
  const a = touches[0]
  const b = touches[1]
  return Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY)
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: LIGHTBOX_Z_INDEX,
  background: 'rgba(0,0,0,0.88)',
  display: 'flex',
  flexDirection: 'column',
  outline: 'none'
}

const toolbarRowStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  alignItems: 'center',
  flexShrink: 0,
  padding: '14px 14px 8px'
}

const toolbarPillStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 4,
  padding: 4,
  borderRadius: 999,
  border: '1px solid var(--glass-border)',
  background: 'var(--bg-elevated)',
  color: 'var(--text-primary)',
  boxShadow: 'var(--shadow-level-3)'
}

const zoomTriggerStyle: CSSProperties = {
  height: 34,
  borderRadius: 17,
  gap: 4,
  padding: '0 10px 0 12px',
  fontVariantNumeric: 'tabular-nums'
}

const bodyStyle: CSSProperties = {
  display: 'flex',
  flex: 1,
  minHeight: 0
}

const gutterStyle: CSSProperties = {
  width: 72,
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center'
}

const navButtonStyle: CSSProperties = {
  color: 'var(--text-primary)',
  background: 'var(--bg-elevated)',
  boxShadow: 'var(--shadow-level-3)'
}

const scrollerStyle: CSSProperties = {
  position: 'relative',
  flex: 1,
  minWidth: 0,
  minHeight: 0,
  touchAction: 'pan-x pan-y'
}

const fitStageStyle: CSSProperties = {
  width: '100%',
  height: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  boxSizing: 'border-box'
}

const zoomStageStyle: CSSProperties = {
  width: 'max-content',
  minWidth: '100%',
  minHeight: '100%',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  boxSizing: 'border-box'
}

const imageStyle: CSSProperties = {
  display: 'block',
  flexShrink: 0,
  objectFit: 'contain',
  borderRadius: 8,
  userSelect: 'none'
}

const loadFailedStyle: CSSProperties = {
  padding: '10px 14px',
  borderRadius: 10,
  background: 'var(--bg-elevated)',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)'
}

const footerStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'center',
  alignItems: 'center',
  flexShrink: 0,
  minHeight: 64,
  padding: '8px 16px 16px'
}

const counterStyle: CSSProperties = {
  padding: '6px 12px',
  borderRadius: 999,
  border: '1px solid var(--glass-border)',
  background: 'var(--bg-elevated)',
  color: 'var(--text-primary)',
  boxShadow: 'var(--shadow-level-3)',
  fontSize: 'var(--type-secondary-size)',
  fontVariantNumeric: 'tabular-nums',
  userSelect: 'none'
}
