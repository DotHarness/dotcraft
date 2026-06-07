import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import {
  ChevronLeft,
  ChevronRight,
  X,
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
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)
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
  const eyebrowVersion = activeRelease?.version ?? ''

  useEffect(() => {
    setFailedMediaIds(new Set())
    setActiveIndex(0)
  }, [releaseKey])

  useEffect(() => {
    closeButtonRef.current?.focus()
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
        <header style={headerStyle}>
          <div style={{ minWidth: 0 }}>
            <div style={eyebrowStyle}>
              <Sparkles size={15} strokeWidth={2} aria-hidden="true" />
              <span>{eyebrowVersion ? t('whatsNew.subtitle', { version: eyebrowVersion }) : t('whatsNew.open')}</span>
            </div>
            <h2 id="whats-new-title" style={titleStyle}>
              {t('whatsNew.title')}
            </h2>
          </div>
          <button
            ref={closeButtonRef}
            className="whats-new-close-button"
            type="button"
            aria-label={t('whatsNew.closeAria')}
            onClick={onClose}
            style={iconButtonStyle}
          >
            <X size={18} strokeWidth={2} aria-hidden="true" />
          </button>
        </header>

        <div style={contentStyle}>
          {releases.length === 0 ? (
            <div style={emptyStyle}>
              <Sparkles size={28} strokeWidth={1.8} aria-hidden="true" />
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
              <button
                type="button"
                onClick={() => setActiveIndex((index) => index + 1)}
                style={navButtonStyle}
              >
                <ChevronLeft size={14} strokeWidth={2} aria-hidden="true" />
                <span>{t('whatsNew.showOlder', { version: olderRelease.version })}</span>
              </button>
            )}
            {newerRelease && (
              <button
                type="button"
                onClick={() => setActiveIndex((index) => index - 1)}
                style={navButtonStyle}
              >
                <span>{t('whatsNew.showNewer', { version: newerRelease.version })}</span>
                <ChevronRight size={14} strokeWidth={2} aria-hidden="true" />
              </button>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            style={primaryButtonStyle}
          >
            {t('whatsNew.close')}
          </button>
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
          <button
            type="button"
            onClick={() => {
              void window.api.shell.openExternal(card.docsUrl as string)
            }}
            style={docsButtonStyle}
          >
            <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
            {t('whatsNew.docs')}
          </button>
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
  background: 'rgba(6, 10, 18, 0.62)',
  backdropFilter: 'blur(10px)'
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
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 16,
  padding: '20px 22px 14px',
  borderBottom: '1px solid var(--border-subtle)'
}

const eyebrowStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 7,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const titleStyle: CSSProperties = {
  margin: '6px 0 0',
  fontSize: 22,
  lineHeight: '30px',
  fontWeight: 680
}

const iconButtonStyle: CSSProperties = {
  width: 32,
  height: 32,
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: 'none',
  borderRadius: 6,
  background: 'var(--whats-new-close-bg, transparent)',
  color: 'var(--whats-new-close-text, var(--text-secondary))',
  transition: 'background-color 120ms ease, color 120ms ease',
  cursor: 'pointer'
}

const contentStyle: CSSProperties = {
  padding: '16px 22px 20px',
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
  fontSize: 15,
  lineHeight: '21px',
  fontWeight: 660,
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
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  marginTop: 4,
  padding: '5px 8px',
  border: '1px solid var(--border-subtle)',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  cursor: 'pointer'
}

const emptyStyle: CSSProperties = {
  minHeight: 220,
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 8,
  color: 'var(--text-secondary)',
  textAlign: 'center'
}

const emptyTitleStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-primary)',
  fontSize: 16,
  lineHeight: '22px',
  fontWeight: 650
}

const emptyBodyStyle: CSSProperties = {
  maxWidth: 360,
  margin: 0,
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const footerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '14px 22px',
  borderTop: '1px solid var(--border-subtle)'
}

const footerNavStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8
}

const navButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  padding: '6px 10px',
  border: '1px solid var(--border-subtle)',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  fontWeight: 600,
  cursor: 'pointer'
}

const primaryButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  height: '32px',
  padding: '0 12px',
  border: '1px solid var(--text-primary)',
  borderRadius: '8px',
  backgroundColor: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  fontSize: '13px',
  fontWeight: 600,
  boxSizing: 'border-box',
  cursor: 'pointer'
}
