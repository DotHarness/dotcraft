import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
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
import { ModalHeader } from '../ui/ModalHeader'

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

  const dialog = (
    <div
      style={backdropStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) {
          onClose()
        }
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="whats-new-title"
        style={dialogStyle}
      >
        <ModalHeader
          icon={<Sparkles size={18} aria-hidden />}
          title={t('whatsNew.title')}
          titleId="whats-new-title"
          description={activeRelease ? t('whatsNew.subtitle', { version: activeRelease.version }) : undefined}
          onClose={onClose}
          closeLabel={t('whatsNew.closeAria')}
          style={headerStyle}
        />

        <div style={contentStyle}>
          {releases.length === 0 ? (
            <div style={emptyStyle}>
              <h3 style={emptyTitleStyle}>{t('whatsNew.emptyTitle')}</h3>
              <p style={emptyBodyStyle}>{t('whatsNew.emptyBody')}</p>
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
        </div>

        <footer style={footerStyle}>
          <div style={footerNavStyle}>
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
          </div>
          <Button
            variant="primary"
            onClick={onClose}
          >
            {t('whatsNew.close')}
          </Button>
        </footer>
      </section>
    </div>
  )

  return createPortal(dialog, document.body)
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
      <div style={cardGridStyle}>
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
    <article style={cardStyle}>
      <div
        style={mediaFrameStyle}
        {...(showPreview ? {} : { role: 'img', 'aria-label': t('whatsNew.mediaLoading') })}
      >
        {showPreview ? (
          <img
            src={mediaUrl as string}
            alt=""
            loading="lazy"
            onError={onMediaFailed}
            style={mediaImageStyle}
          />
        ) : (
          <Skeleton width="100%" height="100%" radius={0} style={{ display: 'block' }} />
        )}
      </div>
      <div style={cardBodyStyle}>
        <h4 style={cardTitleStyle}>{title}</h4>
        <p style={cardSummaryStyle}>{summary}</p>
        {card.docsUrl && (
          <Button
            variant="ghost"
            size="sm"
            style={docsButtonStyle}
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

const backdropStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 2000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  background: 'var(--overlay-scrim)'
}

const dialogStyle: CSSProperties = {
  width: 'min(900px, calc(100vw - 32px))',
  maxHeight: 'min(760px, calc(100vh - 32px))',
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden',
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  boxShadow: '0 24px 80px rgba(0, 0, 0, 0.38)'
}

const headerStyle: CSSProperties = {
  margin: '20px 22px 16px'
}

const contentStyle: CSSProperties = {
  minHeight: 0,
  padding: '0 22px',
  overflowY: 'auto'
}

const cardGridStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 235px), 1fr))',
  gap: 12
}

const cardStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  display: 'flex',
  flexDirection: 'column',
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-secondary)'
}

const mediaFrameStyle: CSSProperties = {
  position: 'relative',
  width: '100%',
  aspectRatio: '16 / 9',
  overflow: 'hidden',
  borderBottom: '1px solid var(--border-subtle)',
  // Lighter than the skeleton's --bg-tertiary so the loading pulse stays visible.
  background: 'var(--bg-secondary)'
}

const mediaImageStyle: CSSProperties = {
  width: '100%',
  height: '100%',
  objectFit: 'cover',
  display: 'block'
}

const cardBodyStyle: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 7,
  padding: 12
}

const cardTitleStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-primary)',
  fontSize: 'var(--type-heading-size)',
  lineHeight: 'var(--type-heading-line-height)',
  fontWeight: 600,
  overflowWrap: 'anywhere'
}

const cardSummaryStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  overflowWrap: 'anywhere'
}

const docsButtonStyle: CSSProperties = {
  alignSelf: 'flex-start',
  marginLeft: -10
}

const emptyStyle: CSSProperties = {
  minHeight: 220,
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 6,
  color: 'var(--text-secondary)',
  textAlign: 'center'
}

const emptyTitleStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-primary)',
  fontSize: 'var(--type-heading-size)',
  lineHeight: 'var(--type-heading-line-height)',
  fontWeight: 600
}

const emptyBodyStyle: CSSProperties = {
  maxWidth: 360,
  margin: 0,
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const footerStyle: CSSProperties = {
  display: 'flex',
  flex: '0 0 auto',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '18px 22px 20px'
}

const footerNavStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  marginLeft: -10
}
