import { useId, useLayoutEffect, useState, type HTMLAttributes, type JSX, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import ReactMarkdown, { type Components } from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Bookmark, Globe2, Image as ImageIcon } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { FileTypeIcon } from '../../ui/FileTypeIcon'
import { IconButton } from '../../ui/IconButton'
import { Skeleton } from '../../ui/Skeleton'
import { OriginIcon } from '../MessageOriginMarkers'
import type { TurnNavigationEntry } from './navigationIndex'
import type { TurnOutput } from './turnOutputs'
import './turn-preview-card.css'

const VIEWPORT_MARGIN_PX = 8
const VISIBLE_OUTPUTS = 2

const OUTPUT_FALLBACK_LABEL = {
  file: 'turnNavigation.outputFile',
  webPreview: 'turnNavigation.outputWebPreview',
  image: 'turnNavigation.outputImage'
} as const

const plainResponseComponents: Components = {
  a: ({ children }) => <>{children}</>,
  img: ({ alt }) => <>{alt ?? ''}</>,
  code: ({ children }) => <>{children}</>,
  pre: ({ children }) => <>{children}</>,
  table: ({ children }) => <table className="turn-preview-card__table">{children}</table>
}

interface TurnPreviewCardProps
  extends Pick<HTMLAttributes<HTMLDivElement>, 'onPointerEnter' | 'onPointerLeave' | 'onBlur' | 'onKeyDown'> {
  id: string
  entry: TurnNavigationEntry
  bookmarked: boolean
  onToggleBookmark: (() => void) | null
  cardRef: RefObject<HTMLDivElement | null>
  toggleRef: RefObject<HTMLButtonElement | null>
  getAnchor: () => HTMLElement | null
}

function clamp(value: number, min: number, max: number): number {
  return max < min ? min : Math.min(Math.max(value, min), max)
}

export function TurnPreviewCard({
  id,
  entry,
  bookmarked,
  onToggleBookmark,
  cardRef,
  toggleRef,
  getAnchor,
  ...handlers
}: TurnPreviewCardProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const [position, setPosition] = useState<{ left: number; top: number } | null>(null)

  useLayoutEffect(() => {
    const card = cardRef.current
    const anchor = getAnchor()
    if (!card || !anchor) return
    const bounds = anchor.getBoundingClientRect()
    const left = clamp(bounds.right, VIEWPORT_MARGIN_PX, window.innerWidth - card.offsetWidth - VIEWPORT_MARGIN_PX)
    const top = clamp(
      bounds.top + bounds.height / 2 - card.offsetHeight / 2,
      VIEWPORT_MARGIN_PX,
      window.innerHeight - card.offsetHeight - VIEWPORT_MARGIN_PX
    )
    setPosition((current) => current?.left === left && current.top === top ? current : { left, top })
  }, [cardRef, entry, getAnchor])

  const content = entry.content
  const loading = !content && !entry.previewFailed
  const response = content?.response.trim() ?? ''

  return createPortal(
    <div
      id={id}
      ref={cardRef}
      role="dialog"
      aria-labelledby={titleId}
      className="turn-preview-card"
      style={position ?? { visibility: 'hidden' }}
      {...handlers}
    >
      <div className="turn-preview-card__label-row">
        {content?.automation && <OriginIcon kind="automation" />}
        {loading ? (
          <div id={titleId} role="status" aria-label={t('turnNavigation.loadingPreview')} className="turn-preview-card__label">
            <Skeleton width="60%" />
          </div>
        ) : (
          <span id={titleId} className="turn-preview-card__label">
            {content ? content.label || t('turnNavigation.noContent') : t('turnNavigation.previewUnavailable')}
          </span>
        )}
        <IconButton
          ref={toggleRef}
          size={18}
          radius={6}
          label={t(bookmarked ? 'turnNavigation.removeBookmark' : 'turnNavigation.bookmark')}
          aria-pressed={bookmarked}
          disabled={!onToggleBookmark}
          icon={<Bookmark size={13} strokeWidth={1.8} fill={bookmarked ? 'currentColor' : 'none'} aria-hidden />}
          onMouseDown={(event) => event.preventDefault()}
          onClick={onToggleBookmark ?? undefined}
        />
      </div>
      {loading ? (
        <div className="turn-preview-card__skeleton-body">
          {['100%', '92%', '80%'].map((width) => (
            <div key={width} className="turn-preview-card__skeleton-line">
              <Skeleton width={width} />
            </div>
          ))}
        </div>
      ) : response && (
        <div className="turn-preview-card__response">
          <ReactMarkdown remarkPlugins={[remarkGfm]} components={plainResponseComponents}>
            {response}
          </ReactMarkdown>
        </div>
      )}
      <PreviewOutputs outputs={entry.outputs} />
    </div>,
    document.body
  )
}

function PreviewOutputs({ outputs }: { outputs: readonly TurnOutput[] }): JSX.Element | null {
  const t = useT()
  if (outputs.length === 0) return null
  return (
    <div className="turn-preview-card__outputs">
      {outputs.slice(0, VISIBLE_OUTPUTS).map((output) => (
        <span key={`${output.kind}:${output.label}`} className="turn-preview-card__output">
          {output.kind === 'file' ? (
            <FileTypeIcon path={output.label} size={13} />
          ) : output.kind === 'webPreview' ? (
            <Globe2 size={13} strokeWidth={1.8} aria-hidden />
          ) : (
            <ImageIcon size={13} strokeWidth={1.8} aria-hidden />
          )}
          <span className="turn-preview-card__output-label">
            {output.label || t(OUTPUT_FALLBACK_LABEL[output.kind])}
          </span>
        </span>
      ))}
      {outputs.length > VISIBLE_OUTPUTS && (
        <span className="turn-preview-card__more-outputs">
          {t('turnNavigation.moreOutputs', { count: outputs.length - VISIBLE_OUTPUTS })}
        </span>
      )}
    </div>
  )
}
