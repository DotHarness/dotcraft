import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react'
import { Download, Gauge, Maximize2, Minimize2, Pause, PictureInPicture2, Play, Volume2, VolumeX } from 'lucide-react'
import { useT } from '../../../../contexts/LocaleContext'
import { addToast } from '../../../../stores/toastStore'
import { buildVersionedViewerUrl } from '../../../../utils/viewerUrl'
import { ContextMenu, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { IconButton } from '../../../ui/IconButton'
import { MoreActionsButton } from '../../../ui/MoreActionsButton'
import { FileChangedNotice } from '../FileChangedNotice'
import type { FilePreviewProps } from '../filePreview'
import { UnavailableNotice } from '../UnavailableNotice'
import { useViewerFileVersion } from '../useViewerFileVersion'
import { formatClock, PLAYBACK_RATES, SEEK_STEP_SECONDS, seekTarget } from './videoPlayback'
import styles from './VideoViewer.module.css'

const MENU_HEIGHT_ESTIMATE = 112

export function VideoViewer({ absolutePath, relativePath }: FilePreviewProps): JSX.Element {
  const t = useT()
  const { version, stale, refresh } = useViewerFileVersion(absolutePath)
  const rootRef = useRef<HTMLDivElement>(null)
  const videoRef = useRef<HTMLVideoElement>(null)
  const [attempt, setAttempt] = useState(0)
  const [failed, setFailed] = useState(false)
  const [playing, setPlaying] = useState(false)
  const [time, setTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [muted, setMuted] = useState(false)
  const [volume, setVolume] = useState(1)
  const [rate, setRate] = useState(1)
  const [fullscreen, setFullscreen] = useState(false)
  const [pictureInPicture, setPictureInPicture] = useState(false)
  const [menu, setMenu] = useState<ContextMenuPosition | null>(null)

  useEffect(() => {
    const sync = () => setFullscreen(document.fullscreenElement === rootRef.current)
    document.addEventListener('fullscreenchange', sync)
    return () => document.removeEventListener('fullscreenchange', sync)
  }, [])

  useEffect(() => {
    setFailed(false)
    setPlaying(false)
    setTime(0)
  }, [absolutePath, version])

  useEffect(() => {
    const element = videoRef.current
    if (!element) return
    const enter = () => setPictureInPicture(true)
    const leave = () => setPictureInPicture(false)
    if (element.readyState >= HTMLMediaElement.HAVE_METADATA && Number.isFinite(element.duration)) setDuration(element.duration)
    element.addEventListener('enterpictureinpicture', enter)
    element.addEventListener('leavepictureinpicture', leave)
    return () => {
      element.removeEventListener('enterpictureinpicture', enter)
      element.removeEventListener('leavepictureinpicture', leave)
    }
  }, [absolutePath, version, attempt, failed])

  if (failed) {
    return (
      <UnavailableNotice
        absolutePath={absolutePath}
        relativePath={relativePath}
        reason={{
          kind: 'load',
          onRetry: () => {
            setFailed(false)
            setAttempt((value) => value + 1)
          }
        }}
      />
    )
  }

  const toggle = () => {
    const element = videoRef.current
    if (!element) return
    if (element.paused) void element.play().catch(() => {})
    else element.pause()
  }
  const seekTo = (seconds: number) => {
    const element = videoRef.current
    if (element) element.currentTime = seekTarget(seconds, element.duration)
  }
  const toggleMute = () => {
    const element = videoRef.current
    if (!element) return
    element.muted = !element.muted
    if (!element.muted && element.volume === 0) element.volume = 1
  }
  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => {})
    else void rootRef.current?.requestFullscreen().catch(() => {})
  }
  const togglePictureInPicture = () => {
    const element = videoRef.current
    if (!element) return
    if (document.pictureInPictureElement === element) void document.exitPictureInPicture().catch(() => {})
    else void element.requestPictureInPicture().catch(() => {})
  }
  const saveAs = () => {
    window.api.workspace.viewer.saveCopyAs({ absolutePath }).catch(() => addToast(t('viewer.actionFailed'), 'warning'))
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.target instanceof HTMLButtonElement || (event.target as HTMLElement).dataset.volume) return
    if (event.key === ' ') toggle()
    else if (event.key === 'ArrowLeft') seekTo((videoRef.current?.currentTime ?? 0) - SEEK_STEP_SECONDS)
    else if (event.key === 'ArrowRight') seekTo((videoRef.current?.currentTime ?? 0) + SEEK_STEP_SECONDS)
    else return
    event.preventDefault()
  }

  const progress = duration > 0 ? `${(time / duration) * 100}%` : '0%'
  const audible = muted ? 0 : volume
  const muteLabel = muted || volume === 0 ? t('viewer.video.unmute') : t('viewer.video.mute')
  const fullscreenLabel = fullscreen ? t('viewer.video.exitFullscreen') : t('viewer.video.fullscreen')

  return (
    <div ref={rootRef} className={styles.root} tabIndex={0} onKeyDown={onKeyDown} role="region" aria-label={relativePath}>
      {stale && <FileChangedNotice onRefresh={refresh} />}
      <div className={styles.stage} onClick={toggle}>
        <video
          key={`${version}:${attempt}`}
          ref={videoRef}
          className={styles.video}
          src={buildVersionedViewerUrl(absolutePath, version)}
          preload="metadata"
          playsInline
          onPlay={() => setPlaying(true)}
          onPause={() => setPlaying(false)}
          onTimeUpdate={(event) => setTime(event.currentTarget.currentTime)}
          onDurationChange={(event) => setDuration(Number.isFinite(event.currentTarget.duration) ? event.currentTarget.duration : 0)}
          onVolumeChange={(event) => {
            setMuted(event.currentTarget.muted)
            setVolume(event.currentTarget.volume)
          }}
          onRateChange={(event) => setRate(event.currentTarget.playbackRate)}
          onLoadedMetadata={(event) => {
            event.currentTarget.playbackRate = rate
            event.currentTarget.volume = volume
            event.currentTarget.muted = muted
          }}
          onError={() => setFailed(true)}
        />
      </div>
      <div className={styles.controls}>
        <IconButton
          size={28}
          label={playing ? t('viewer.video.pause') : t('viewer.video.play')}
          tooltipLabel={playing ? t('viewer.video.pause') : t('viewer.video.play')}
          icon={playing ? <Pause size={16} aria-hidden /> : <Play size={16} aria-hidden />}
          onClick={toggle}
        />
        <input
          className={styles.range}
          type="range"
          min={0}
          max={duration || 0}
          step={0.01}
          value={Math.min(time, duration || 0)}
          disabled={duration === 0}
          aria-label={t('viewer.video.seek')}
          aria-valuetext={`${formatClock(time, duration)} / ${formatClock(duration, duration)}`}
          style={{ '--viewer-video-progress': progress } as CSSProperties}
          onChange={(event) => seekTo(Number(event.currentTarget.value))}
        />
        <span className={styles.time}>{formatClock(time, duration)} / {formatClock(duration, duration)}</span>
        <IconButton
          size={28}
          label={muteLabel}
          tooltipLabel={muteLabel}
          icon={audible === 0 ? <VolumeX size={16} aria-hidden /> : <Volume2 size={16} aria-hidden />}
          onClick={toggleMute}
        />
        <input
          className={`${styles.range} ${styles.volume}`}
          type="range"
          min={0}
          max={1}
          step={0.05}
          value={audible}
          data-volume="true"
          aria-label={t('viewer.video.volume')}
          style={{ '--viewer-video-progress': `${audible * 100}%` } as CSSProperties}
          onChange={(event) => {
            const element = videoRef.current
            if (!element) return
            element.volume = Number(event.currentTarget.value)
            element.muted = element.volume === 0
          }}
        />
        <IconButton
          size={28}
          label={fullscreenLabel}
          tooltipLabel={fullscreenLabel}
          icon={fullscreen ? <Minimize2 size={15} aria-hidden /> : <Maximize2 size={15} aria-hidden />}
          onClick={toggleFullscreen}
        />
        {!fullscreen && (
          <MoreActionsButton
            size={28}
            label={t('viewer.video.options')}
            open={menu != null}
            tooltipPlacement="top"
            onClick={(event) => {
              const rect = event.currentTarget.getBoundingClientRect()
              setMenu({ x: rect.right - 200, y: rect.top - MENU_HEIGHT_ESTIMATE })
            }}
          />
        )}
      </div>
      {menu && (
        <ContextMenu
          position={menu}
          onClose={() => setMenu(null)}
          items={[
            {
              label: t('viewer.video.playbackSpeed'),
              icon: <Gauge size={15} aria-hidden />,
              onClick: () => {},
              submenu: PLAYBACK_RATES.map((value) => ({
                label: `${value}×`,
                selection: 'radio' as const,
                checked: value === rate,
                onClick: () => {
                  setMenu(null)
                  const element = videoRef.current
                  if (element) element.playbackRate = value
                }
              }))
            },
            {
              label: t('viewer.video.pictureInPicture'),
              icon: <PictureInPicture2 size={15} aria-hidden />,
              selection: 'checkbox',
              checked: pictureInPicture,
              disabled: !document.pictureInPictureEnabled,
              onClick: () => {
                setMenu(null)
                togglePictureInPicture()
              }
            },
            {
              label: t('viewer.video.saveAs'),
              icon: <Download size={15} aria-hidden />,
              onClick: () => {
                setMenu(null)
                saveAs()
              }
            }
          ]}
        />
      )}
    </div>
  )
}
