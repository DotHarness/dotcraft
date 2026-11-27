import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Code2, Maximize2 } from 'lucide-react'
import { useDocumentThemeMode } from '../../utils/theme'
import { useT } from '../../contexts/LocaleContext'
import { CopyButton } from '../ui/CopyButton'
import { IconButton } from '../ui/IconButton'
import { RunningShimmer } from '../ui/RunningShimmer'
import { Skeleton } from '../ui/Skeleton'
import { ImageLightbox } from './ImageLightbox'
import type { GalleryImage } from './imagePreview/galleryImages'
import { mermaidSvgToImageSrc } from './mermaidImage'
import {
  buildMermaidCacheKey,
  clearMermaidSvgCache,
  getCachedMermaidSvg,
  getMermaidBackgroundColor,
  normalizeMermaidSource,
  renderMermaidSvg
} from './mermaidRender'

export { clearMermaidSvgCache, renderMermaidSvg }

interface MermaidDiagramProps {
  source: string
  streaming?: boolean
  renderSource: (leadingActions?: ReactNode) => ReactNode
}

type MermaidRenderState =
  | { status: 'loading' }
  | { status: 'ready'; svg: string; key: string; normalizedSource: string }
  | { status: 'error'; error: string; key: string }

let nextMermaidId = 0

export function MermaidDiagram({
  source,
  streaming = false,
  renderSource
}: MermaidDiagramProps): JSX.Element {
  const t = useT()
  const themeMode = useDocumentThemeMode()
  const idRef = useRef<string>(`dc-mermaid-${nextMermaidId++}`)
  const normalizedSource = normalizeMermaidSource(source)
  const cacheKey = buildMermaidCacheKey(normalizedSource, themeMode)
  const cachedSvg = streaming ? undefined : getCachedMermaidSvg(normalizedSource, themeMode)

  const [renderState, setRenderState] = useState<MermaidRenderState>({ status: 'loading' })
  const [showSource, setShowSource] = useState(false)
  const [fullscreen, setFullscreen] = useState<GalleryImage | null>(null)

  useEffect(() => {
    if (streaming || cachedSvg !== undefined) return

    if (!normalizedSource) {
      setRenderState({ status: 'error', error: 'Empty Mermaid diagram.', key: cacheKey })
      return
    }

    let cancelled = false
    setRenderState((current) =>
      current.status === 'ready' && current.normalizedSource === normalizedSource
        ? current
        : { status: 'loading' }
    )

    void renderMermaidSvg({
      id: idRef.current,
      source,
      themeMode
    }).then((svg) => {
      if (!cancelled) {
        setRenderState({ status: 'ready', svg, key: cacheKey, normalizedSource })
      }
    }).catch((error: unknown) => {
      if (!cancelled) {
        setRenderState({
          status: 'error',
          error: error instanceof Error ? error.message : String(error),
          key: cacheKey
        })
      }
    })

    return () => {
      cancelled = true
    }
  }, [cacheKey, cachedSvg, normalizedSource, source, streaming, themeMode])

  if (streaming) {
    return (
      <div className="dc-mermaid dc-mermaid--streaming" data-mermaid-streaming="" role="status">
        <RunningShimmer className="dc-mermaid__streaming-label">
          {t('markdown.mermaid.streaming')}
        </RunningShimmer>
      </div>
    )
  }

  const effectiveState: MermaidRenderState = cachedSvg !== undefined
    ? { status: 'ready', svg: cachedSvg, key: cacheKey, normalizedSource }
    : renderState.status === 'ready' && (renderState.key === cacheKey || renderState.normalizedSource === normalizedSource)
      ? renderState
      : renderState.status === 'error' && renderState.key === cacheKey
        ? renderState
        : { status: 'loading' }

  const showSourceLabel = t('markdown.mermaid.showSource')
  const viewDiagramLabel = t('markdown.mermaid.viewDiagram')
  const fullscreenLabel = t('markdown.mermaid.openFullscreen')

  const openFullscreen = (svg: string): void => {
    const src = mermaidSvgToImageSrc(svg, getMermaidBackgroundColor(themeMode))
    if (src) setFullscreen({ key: idRef.current, src, title: t('markdown.mermaid.diagramAria') })
  }

  const sourceToggleToDiagram = (
    <IconButton
      size={24}
      radius={6}
      icon={<Code2 size={14} aria-hidden />}
      label={viewDiagramLabel}
      tooltipLabel={viewDiagramLabel}
      tooltipPlacement="top"
      active
      activeTone="neutral"
      aria-pressed
      onClick={() => setShowSource(false)}
    />
  )

  return (
    <div className="dc-mermaid">
      {effectiveState.status === 'loading' ? (
        <div
          role="status"
          aria-busy="true"
          aria-label={t('markdown.mermaid.loading')}
          className="dc-mermaid__skeleton"
        >
          <Skeleton width="100%" height="100%" radius={10} />
        </div>
      ) : effectiveState.status === 'ready' ? (
        showSource ? (
          renderSource(sourceToggleToDiagram)
        ) : (
          <>
            <div className="dc-mermaid__actions">
              <IconButton
                size={24}
                radius={6}
                icon={<Code2 size={14} aria-hidden />}
                label={showSourceLabel}
                tooltipLabel={showSourceLabel}
                tooltipPlacement="top"
                aria-pressed={false}
                onClick={() => setShowSource(true)}
              />
              <CopyButton
                getText={() => source}
                label={t('markdown.mermaid.copySource')}
                copiedLabel={t('markdown.mermaid.copiedSource')}
              />
              <IconButton
                size={24}
                radius={6}
                icon={<Maximize2 size={14} aria-hidden />}
                label={fullscreenLabel}
                tooltipLabel={fullscreenLabel}
                tooltipPlacement="top"
                onClick={() => openFullscreen(effectiveState.svg)}
              />
            </div>
            <div
              className="dc-mermaid-diagram"
              data-testid="mermaid-diagram"
              role="img"
              aria-label={t('markdown.mermaid.diagramAria')}
              dangerouslySetInnerHTML={{ __html: effectiveState.svg }}
            />
          </>
        )
      ) : (
        <div className="dc-mermaid__fallback">
          {renderSource()}
          <div role="status" title={effectiveState.error} className="dc-mermaid__error">
            {t('markdown.mermaid.renderFailed')}
          </div>
        </div>
      )}
      {fullscreen && (
        <ImageLightbox images={[fullscreen]} initialIndex={0} onClose={() => setFullscreen(null)} />
      )}
    </div>
  )
}
