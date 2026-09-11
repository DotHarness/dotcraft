import { useEffect, useRef, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { Minimize2, X } from 'lucide-react'
import { requestedFrameWidth } from '../../../../shared/screenView'
import { useT } from '../../../contexts/LocaleContext'
import { LayerBoundary } from '../../../contexts/LayerContext'
import { IconButton } from '../../ui/IconButton'
import { useScreenViewStore } from '../../../stores/screenViewStore'
import { ScreenViewStateSurface } from './ScreenViewStateSurface'
import { screenViewStateLabelKey, screenViewStateTitle } from './screenViewLabels'
import type { ScreenStream } from './useScreenStream'
import styles from './ScreenViewTheater.module.css'

interface ScreenViewTheaterProps {
  hostName: string
  stream: ScreenStream
}

export function ScreenViewTheater({ hostName, stream }: ScreenViewTheaterProps): JSX.Element {
  const t = useT()
  const setMode = useScreenViewStore((s) => s.setMode)
  const tuneWidth = useScreenViewStore((s) => s.tuneWidth)
  const close = useScreenViewStore((s) => s.close)
  const dialogRef = useRef<HTMLDialogElement>(null)
  const stageRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    dialog.showModal()
    return () => dialog.close()
  }, [])

  useEffect(() => {
    const stage = stageRef.current
    if (!stage) return
    const measure = (): void => tuneWidth(requestedFrameWidth(stage.clientWidth, window.devicePixelRatio))
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    measure()
    return () => observer.disconnect()
  }, [tuneWidth])

  const title = screenViewStateTitle(hostName, t(screenViewStateLabelKey(stream.state)), stream.state)
  const style: CSSProperties & Record<'--screen-aspect', string> = {
    '--screen-aspect': stream.aspect ? `${stream.aspect}` : '16 / 9'
  }

  const theater = (
    <dialog
      ref={dialogRef}
      className={styles.theater}
      style={style}
      aria-label={title}
      title={title}
      onCancel={(event) => {
        event.preventDefault()
        setMode('dock')
      }}
      onClick={(event) => {
        if (event.target === dialogRef.current) setMode('dock')
      }}
    >
      <div className={styles.chrome}>
        <span className={styles.name}>{hostName}</span>
        <IconButton
          size={26}
          label={t('screenView.exitTheater')}
          onClick={() => setMode('dock')}
          icon={<Minimize2 size={15} aria-hidden />}
        />
        <IconButton
          size={26}
          label={t('screenView.close')}
          onClick={close}
          icon={<X size={15} aria-hidden />}
        />
      </div>
      <ScreenViewStateSurface status={stream.state} className={styles.stage} surfaceRef={stageRef}>
        <canvas ref={stream.setCanvas} className={styles.canvas} aria-label={title} role="img" />
      </ScreenViewStateSurface>
    </dialog>
  )

  return createPortal(<LayerBoundary blocksNativeViews>{theater}</LayerBoundary>, document.body)
}
