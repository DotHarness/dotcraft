import { useEffect, useId, useMemo, useState } from 'react'
import {
  ChevronLeft,
  ChevronRight,
  Sparkles,
  ExternalLink
} from 'lucide-react'

import type { AppLocale } from '../../../shared/locales'
import {
  getLocalizedWhatsNewText,
  getWhatsNewMediaStateKey,
  type WhatsNewCard,
  type WhatsNewMediaState,
  type WhatsNewRelease
} from '../../../shared/whatsNew'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { Skeleton } from '../ui/Skeleton'
import { Button, ButtonLabel } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import styles from './WhatsNewDialog.module.css'

interface WhatsNewDialogProps {
  releases: WhatsNewRelease[]
  mediaStates: Record<string, WhatsNewMediaState>
  onClose: () => void
}

export function WhatsNewDialog({
  releases,
  mediaStates,
  onClose
}: WhatsNewDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const locale = useLocale()
  const releaseKey = useMemo(
    () => releases.map((release) => `${release.version}:${release.cards.map((card) => card.id).join(',')}`).join('|'),
    [releases]
  )
  const [failedMediaIds, setFailedMediaIds] = useState<Set<string>>(() => new Set())
  const [activeIndex, setActiveIndex] = useState(0)
  const activeRelease = releases[activeIndex]
  const newerRelease = activeIndex > 0 ? releases[activeIndex - 1] : null
  const olderRelease =
    activeIndex < releases.length - 1 ? releases[activeIndex + 1] : null

  useEffect(() => {
    setFailedMediaIds(new Set())
    setActiveIndex(0)
  }, [releaseKey])

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <ModalDialog
      titleId={titleId}
      width={900}
      maxHeight={760}
      onDismiss={onClose}
      header={(
        <ModalHeader
          icon={<Sparkles size={18} aria-hidden />}
          title={t('whatsNew.title')}
          titleId={titleId}
          description={activeRelease ? t('whatsNew.subtitle', { version: activeRelease.version }) : undefined}
          onClose={onClose}
          closeLabel={t('whatsNew.closeAria')}
        />
      )}
      footer={(
        <ModalFooter
          leading={olderRelease || newerRelease ? (
            <>
              {olderRelease && (
                <Button
                  variant="ghost"
                  onClick={() => setActiveIndex((index) => index + 1)}
                >
                  <ChevronLeft size={14} strokeWidth={2} aria-hidden="true" />
                  <ButtonLabel>{t('whatsNew.showOlder', { version: olderRelease.version })}</ButtonLabel>
                </Button>
              )}
              {newerRelease && (
                <Button
                  variant="ghost"
                  onClick={() => setActiveIndex((index) => index - 1)}
                >
                  <ButtonLabel>{t('whatsNew.showNewer', { version: newerRelease.version })}</ButtonLabel>
                  <ChevronRight size={14} strokeWidth={2} aria-hidden="true" />
                </Button>
              )}
            </>
          ) : undefined}
        >
          <Button variant="primary" onClick={onClose}>
            {t('whatsNew.close')}
          </Button>
        </ModalFooter>
      )}
    >
      {releases.length === 0 ? (
        <div className={styles.empty}>
          <h3 className={styles.emptyTitle}>{t('whatsNew.emptyTitle')}</h3>
          <p className={styles.emptyBody}>{t('whatsNew.emptyBody')}</p>
        </div>
      ) : (
        activeRelease && (
          <ReleaseSection
            key={activeRelease.version}
            release={activeRelease}
            locale={locale}
            mediaStates={mediaStates}
            failedMediaIds={failedMediaIds}
            onMediaFailed={(key) => {
              setFailedMediaIds((current) => {
                const next = new Set(current)
                next.add(key)
                return next
              })
            }}
          />
        )
      )}
    </ModalDialog>
  )
}

function ReleaseSection({
  release,
  locale,
  mediaStates,
  failedMediaIds,
  onMediaFailed
}: {
  release: WhatsNewRelease
  locale: AppLocale
  mediaStates: Record<string, WhatsNewMediaState>
  failedMediaIds: Set<string>
  onMediaFailed: (key: string) => void
}): JSX.Element {
  const t = useT()
  return (
    <section aria-label={t('whatsNew.versionLabel', { version: release.version })}>
      <div className={styles.cardGrid}>
        {release.cards.map((card) => {
          const key = getWhatsNewMediaStateKey(release.version, card.id)
          return (
            <WhatsNewCardView
              key={card.id}
              card={card}
              locale={locale}
              mediaState={mediaStates[key]}
              mediaFailed={failedMediaIds.has(key)}
              onMediaFailed={() => onMediaFailed(key)}
            />
          )
        })}
      </div>
    </section>
  )
}

function WhatsNewCardView({
  card,
  locale,
  mediaState,
  mediaFailed,
  onMediaFailed
}: {
  card: WhatsNewCard
  locale: AppLocale
  mediaState?: WhatsNewMediaState
  mediaFailed: boolean
  onMediaFailed: () => void
}): JSX.Element {
  const t = useT()
  const title = getLocalizedWhatsNewText(card.title, locale)
  const summary = getLocalizedWhatsNewText(card.summary, locale)
  const mediaUrl = card.media && mediaState?.status === 'ready'
    ? mediaState.cachedUrl ?? null
    : null
  const showPreview = Boolean(mediaUrl) && !mediaFailed

  return (
    <article className={styles.card}>
      <div
        className={styles.mediaFrame}
        {...(showPreview ? {} : { role: 'img', 'aria-label': t('whatsNew.mediaLoading') })}
      >
        {showPreview ? (
          <img
            src={mediaUrl as string}
            alt=""
            loading="lazy"
            onError={onMediaFailed}
            className={styles.mediaImage}
          />
        ) : (
          <Skeleton width="100%" height="100%" radius={0} style={{ display: 'block' }} />
        )}
      </div>
      <div className={styles.cardBody}>
        <h4 className={styles.cardTitle}>{title}</h4>
        <p className={styles.cardSummary}>{summary}</p>
        {card.docsUrl && (
          <Button
            variant="ghost"
            size="sm"
            className={styles.docsButton}
            onClick={() => {
              void window.api.shell.openExternal(card.docsUrl as string)
            }}
          >
            <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
            {t('whatsNew.docs')}
          </Button>
        )}
      </div>
    </article>
  )
}
