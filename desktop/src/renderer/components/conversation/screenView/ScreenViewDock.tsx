import { useEffect, useRef, type CSSProperties, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { Maximize2, X } from 'lucide-react'
import { requestedFrameWidth } from '../../../../shared/screenView'
import { useT } from '../../../contexts/LocaleContext'
import { IconButton } from '../../ui/IconButton'
import { DOCK_WIDTH_MAX, DOCK_WIDTH_MIN, useScreenViewStore } from '../../../stores/screenViewStore'
import { useDockGeometry } from './hooks/useDockGeometry'
import { ScreenViewStateSurface } from './ScreenViewStateSurface'
import { screenViewStateLabelKey, screenViewStateTitle } from './screenViewLabels'
import type { ScreenStream } from './useScreenStream'
import styles from './ScreenViewDock.module.css'

interface ScreenViewDockProps {
  hostName: string
  stream: ScreenStream
  anchorRef: RefObject<HTMLButtonElement | null>
}

export function ScreenViewDock({ hostName, stream, anchorRef }: ScreenViewDockProps): JSX.Element {
  const t = useT()
  const dockWidth = useScreenViewStore((s) => s.dockWidth)
  const dockPosition = useScreenViewStore((s) => s.dockPosition)
  const setDockGeometry = useScreenViewStore((s) => s.setDockGeometry)
  const tuneWidth = useScreenViewStore((s) => s.tuneWidth)
  const setMode = useScreenViewStore((s) => s.setMode)
  const close = useScreenViewStore((s) => s.close)
  const rootRef = useRef<HTMLDivElement | null>(null)
  const geometry = useDockGeometry({
    rootRef,
    anchorRef,
    width: dockWidth,
    position: dockPosition,
    commit: setDockGeometry
  })

  const devicePixelRatio = window.devicePixelRatio
  useEffect(() => {
    tuneWidth(requestedFrameWidth(geometry.width, devicePixelRatio))
  }, [devicePixelRatio, geometry.width, tuneWidth])

  const title = screenViewStateTitle(hostName, t(screenViewStateLabelKey(stream.state)), stream.state)
  const style: CSSProperties & Record<'--screen-aspect', string> = {
    width: `${geometry.width}px`,
    left: `${geometry.position.x}px`,
    top: `${geometry.position.y}px`,
    '--screen-aspect': stream.aspect ? `${stream.aspect}` : '16 / 9'
  }

  const dock = (
    <div
      ref={rootRef}
      className={styles.dock}
      style={style}
      data-dragging={geometry.dragging || undefined}
      tabIndex={0}
      aria-label={t('screenView.move')}
      title={title}
      onPointerDown={geometry.onPointerDown}
      onPointerMove={geometry.onPointerMove}
      onPointerUp={geometry.onPointerUp}
      onPointerCancel={geometry.onPointerUp}
      onKeyDown={geometry.onKeyDown}
      onClickCapture={geometry.onClickCapture}
    >
      <div className={styles.chrome}>
        <span className={styles.name}>{hostName}</span>
        <IconButton
          size={22}
          label={t('screenView.theater')}
          onClick={() => setMode('theater')}
          icon={<Maximize2 size={13} aria-hidden />}
        />
        <IconButton
          size={22}
          label={t('screenView.close')}
          onClick={close}
          icon={<X size={13} aria-hidden />}
        />
      </div>

      <ScreenViewStateSurface status={stream.state} className={styles.stage}>
        <canvas ref={stream.setCanvas} className={styles.canvas} aria-label={title} role="img" />
      </ScreenViewStateSurface>

      <div
        role="separator"
        aria-orientation="vertical"
        aria-label={t('screenView.resize')}
        aria-valuemin={DOCK_WIDTH_MIN}
        aria-valuemax={DOCK_WIDTH_MAX}
        aria-valuenow={geometry.width}
        tabIndex={0}
        className={styles.grip}
        onPointerDown={geometry.onGripPointerDown}
        onKeyDown={geometry.onGripKeyDown}
      />
    </div>
  )

  return createPortal(dock, document.body)
}
