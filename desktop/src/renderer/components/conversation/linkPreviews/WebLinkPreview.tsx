import { useCallback, useState, type AnchorHTMLAttributes, type CSSProperties, type JSX, type MouseEvent, type ReactNode } from 'react'
import { ArrowUpRight, CloudOff, Globe, ShieldOff } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useLinkPreview, type LinkPreviewLoad } from '../../../stores/linkPreviewStore'
import { linkPreviewHost, type LinkPreviewError } from '../../../../shared/linkPreviews'
import { Skeleton } from '../../ui/Skeleton'
import { useReferenceHoverCard } from '../referenceHoverCard'

type OpenLink = (url: string, event: MouseEvent<HTMLElement>) => void

const ERROR_ICONS = { unavailable: CloudOff, blocked: ShieldOff } satisfies Record<LinkPreviewError, unknown>

function Favicon({ src }: { src?: string }): JSX.Element {
  const [failed, setFailed] = useState(false)
  return src && !failed ? (
    <img className="dc-link-card__favicon" src={src} alt="" aria-hidden onError={() => setFailed(true)} />
  ) : (
    <Globe className="dc-link-card__favicon dc-link-card__favicon--glyph" size={14} strokeWidth={1.9} aria-hidden />
  )
}

export function LinkPreviewCardBody({ url, load, onOpenLink }: { url: string; load: LinkPreviewLoad; onOpenLink: OpenLink }): JSX.Element {
  const t = useT()
  const ready = load.status === 'ready' ? load : null
  const ErrorIcon = load.status === 'error' ? ERROR_ICONS[load.error] : null
  return (
    <>
      <div className="dc-link-card__header">
        {load.status === 'loading' ? <Skeleton width={16} height={16} radius={4} /> : <Favicon src={ready?.faviconDataUrl} />}
        <span className="dc-link-card__host">{linkPreviewHost(url)}</span>
        <button type="button" className="dc-link-card__open" onClick={(event) => onOpenLink(url, event)}>
          {t('linkPreview.open')}
          <ArrowUpRight size={13} strokeWidth={2} aria-hidden />
        </button>
      </div>
      {load.status === 'loading' && (
        <div role="status" aria-label={t('linkPreview.loading')}>
          <div className="dc-link-card__media">
            <Skeleton width="100%" height="100%" radius={0} />
          </div>
          <div className="dc-link-card__title-skeleton">
            <Skeleton width="88%" height={12} />
            <Skeleton width="54%" height={12} />
          </div>
        </div>
      )}
      {load.status === 'error' && ErrorIcon && (
        <p className="dc-link-card__error">
          <ErrorIcon size={14} strokeWidth={1.8} aria-hidden />
          <span>{t(`linkPreview.error.${load.error}`)}</span>
        </p>
      )}
      {ready && (
        <>
          {ready.imageDataUrl && ready.title && (
            <div className="dc-link-card__media">
              <img src={ready.imageDataUrl} alt="" draggable={false} />
            </div>
          )}
          <a
            href={url}
            className="dc-link-card__title"
            data-untitled={ready.title ? undefined : 'true'}
            tabIndex={-1}
            draggable={false}
            onClick={(event) => {
              event.preventDefault()
              onOpenLink(url, event)
            }}
          >
            {ready.title ?? url}
          </a>
        </>
      )}
    </>
  )
}

export function WebLinkPreviewAnchor({
  href,
  className,
  style,
  linkProps,
  onOpenLink,
  children
}: {
  href: string
  className: string
  style: CSSProperties
  linkProps: AnchorHTMLAttributes<HTMLAnchorElement>
  onOpenLink: OpenLink
  children: ReactNode
}): JSX.Element {
  const [opened, setOpened] = useState(false)
  const load = useLinkPreview(href, opened)
  const label = load.status === 'ready' && load.title ? load.title : linkPreviewHost(href)
  const onOpen = useCallback(() => setOpened(true), [])
  const { anchorProps, card } = useReferenceHoverCard<HTMLAnchorElement>({
    label,
    className: 'dc-link-card dc-link-card--floating',
    layoutKey: load,
    compact: load.status === 'error',
    onOpen,
    children: <LinkPreviewCardBody url={href} load={load} onOpenLink={onOpenLink} />
  })

  return (
    <>
      <a className={className} style={style} {...linkProps} {...anchorProps}>
        {children}
      </a>
      {card}
    </>
  )
}
