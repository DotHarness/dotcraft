import { useEffect, useId, type CSSProperties, type ReactNode } from 'react'
import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useDocumentThemeMode } from '../../utils/theme'
import { Button } from '../ui/Button'
import { ModalDialog } from '../ui/ModalDialog'
import fieldDark from '../../assets/featureGuides/field-dark.webp'
import fieldLight from '../../assets/featureGuides/field-light.webp'
import styles from './FeatureGuide.module.css'

export interface FeatureGuideMediaSource {
  light: string
  dark: string
}

interface FeatureGuideContent {
  media: ReactNode
  title: string
  body: string
  primaryLabel: string
  onPrimary: () => void
  onClose: () => void
}

export function FeatureGuideMedia({ focus = '50% 40%', height, children }: { focus?: string; height: number; children: ReactNode }): JSX.Element {
  const theme = useDocumentThemeMode()
  return (
    <div
      className={styles.media}
      style={{ height, backgroundImage: `url(${theme === 'dark' ? fieldDark : fieldLight})`, backgroundPosition: focus }}
      aria-hidden
    >
      {children}
    </div>
  )
}

export function FeatureGuideCapture({ source, style }: { source: FeatureGuideMediaSource; style: CSSProperties }): JSX.Element {
  const theme = useDocumentThemeMode()
  return <img className={styles.capture} src={theme === 'dark' ? source.dark : source.light} alt="" draggable={false} style={style} />
}

function MediaCloseButton({ onClose }: { onClose: () => void }): JSX.Element {
  const t = useT()
  return (
    <button type="button" className={styles.close} aria-label={t('featureGuides.close')} onClick={onClose}>
      <X size={16} aria-hidden />
    </button>
  )
}

function useEscape(onClose: () => void): void {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      event.stopPropagation()
      onClose()
    }
    window.addEventListener('keydown', onKeyDown, true)
    return () => window.removeEventListener('keydown', onKeyDown, true)
  }, [onClose])
}

export function FeatureGuideCoachmark({
  media,
  title,
  body,
  primaryLabel,
  onPrimary,
  onClose,
  style
}: FeatureGuideContent & { style?: CSSProperties }): JSX.Element {
  const t = useT()
  const titleId = useId()
  useEscape(onClose)
  return (
    <section className={styles.coachmark} role="dialog" aria-labelledby={titleId} style={style}>
      {media}
      <MediaCloseButton onClose={onClose} />
      <div className={styles.coachmarkBody}>
        <h2 id={titleId} className={styles.title}>{title}</h2>
        <p className={styles.text}>{body}</p>
      </div>
      <div className={styles.coachmarkFooter}>
        <Button variant="ghost" size="sm" onClick={onClose}>{t('featureGuides.notNow')}</Button>
        <Button variant="primary" size="sm" onClick={onPrimary}>{primaryLabel}</Button>
      </div>
    </section>
  )
}

export interface FeatureGuidePoint {
  icon: ReactNode
  title: string
  body: string
}

export function FeatureGuideSpotlight({
  media,
  title,
  body,
  points,
  primaryLabel,
  onPrimary,
  onClose
}: FeatureGuideContent & { points: FeatureGuidePoint[] }): JSX.Element {
  const titleId = useId()
  useEscape(onClose)
  return (
    <ModalDialog
      titleId={titleId}
      width={440}
      onDismiss={onClose}
      className={styles.spotlight}
      header={(
        <div className={styles.spotlightMedia}>
          {media}
          <MediaCloseButton onClose={onClose} />
        </div>
      )}
      footer={(
        <div className="dc-modal__footer">
          <Button variant="primary" onClick={onPrimary}>{primaryLabel}</Button>
        </div>
      )}
    >
      <div>
        <h2 id={titleId} className={styles.title}>{title}</h2>
        <p className={styles.text}>{body}</p>
        <ul className={styles.points}>
          {points.map((point) => (
            <li key={point.title} className={styles.point}>
              <span className={styles.pointIcon} aria-hidden>{point.icon}</span>
              <span><strong>{point.title}</strong> {point.body}</span>
            </li>
          ))}
        </ul>
      </div>
    </ModalDialog>
  )
}
