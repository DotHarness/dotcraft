import { useEffect, useRef, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { IconButton } from '../ui/IconButton'
import styles from './MarkdownTable.module.css'

export function MarkdownTableViewer({ children, onClose }: { children: ReactNode; onClose: () => void }): JSX.Element {
  const t = useT()
  const closeButton = useRef<HTMLButtonElement>(null)
  const controlsTop = (window.api.platform === 'darwin' ? 0 : window.api.titleBarOverlayHeight) + 12

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    closeButton.current?.focus()
    return () => opener?.focus({ preventScroll: true })
  }, [])

  useEffect(() => {
    const onKeyDown = (event: globalThis.KeyboardEvent): void => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  function keepFocus(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Tab' || !event.currentTarget.contains(event.target as Node)) return
    const stops = event.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], [tabindex="0"]')
    const first = stops[0]
    const last = stops[stops.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return createPortal(
    <LayerBoundary blocksNativeViews>
      <div
        className={styles.overlay}
        style={{ '--table-viewer-controls-top': `${controlsTop}px` } as CSSProperties}
        role="dialog"
        aria-modal="true"
        aria-label={t('markdown.table.title')}
        onKeyDown={keepFocus}
        onMouseDown={(event) => { if (event.target === event.currentTarget) onClose() }}
      >
        <IconButton ref={closeButton} className={styles.viewerClose} size={40} radius={20} icon={<X size={18} aria-hidden />} label={t('common.close')} onClick={onClose} />
        <div className={styles.viewer}>
          <div className={`markdown-body ${styles.viewerContent}`} role="region" aria-label={t('markdown.table.title')} tabIndex={0}>{children}</div>
        </div>
      </div>
    </LayerBoundary>,
    document.body
  )
}
