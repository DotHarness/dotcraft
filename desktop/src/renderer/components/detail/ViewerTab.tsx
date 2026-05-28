/**
 * ViewerTab — the container component rendered in the detail panel body when a
 * viewer tab is active.
 *
 * Looks up the tab descriptor from `viewerTabStore` and routes to the
 * appropriate sub-viewer (text, image, pdf, unsupported).  All sub-viewers
 * are `React.lazy`-loaded to keep Monaco out of the initial bundle.
 */
import { lazy, Suspense } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { AlertTriangle } from 'lucide-react'

const LazyTextViewer = lazy(() =>
  import('./viewers/TextViewer').then((m) => ({ default: m.TextViewer }))
)
const LazyMarkdownViewer = lazy(() =>
  import('./viewers/MarkdownViewer').then((m) => ({ default: m.MarkdownViewer }))
)
const LazyBrowserViewerTab = lazy(() =>
  import('./viewers/BrowserViewerTab').then((m) => ({ default: m.BrowserViewerTab }))
)
const LazyTerminalViewerTab = lazy(() =>
  import('./viewers/TerminalViewerTab').then((m) => ({ default: m.TerminalViewerTab }))
)
const LazyImageViewer = lazy(() =>
  import('./viewers/ImageViewer').then((m) => ({ default: m.ImageViewer }))
)
const LazyPdfViewer = lazy(() =>
  import('./viewers/PdfViewer').then((m) => ({ default: m.PdfViewer }))
)
const LazyUnsupportedViewer = lazy(() =>
  import('./viewers/UnsupportedViewer').then((m) => ({ default: m.UnsupportedViewer }))
)

interface ViewerTabProps {
  tabId: string
}

function isMarkdownPath(filePath: string): boolean {
  const normalized = filePath.replace(/\\/g, '/').toLowerCase()
  return normalized.endsWith('.md') || normalized.endsWith('.mdx')
}

export function ViewerTab({ tabId }: ViewerTabProps): JSX.Element {
  const t = useT()
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const tab = useViewerTabStore((s) => {
    if (!currentThreadId) return null
    return s.getThreadState(currentThreadId).tabs.find((t) => t.id === tabId) ?? null
  })

  if (!tab) {
    return (
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        gap: '12px',
        color: 'var(--text-secondary)',
        fontSize: '13px',
        padding: '24px',
        textAlign: 'center'
      }}>
        <AlertTriangle size={24} strokeWidth={1.5} aria-hidden style={{ opacity: 0.5 }} />
        {t('viewer.missingFile')}
      </div>
    )
  }

  if (tab.errorMessage) {
    return (
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        gap: '12px',
        color: 'var(--text-secondary)',
        fontSize: '13px',
        padding: '24px',
        textAlign: 'center'
      }}>
        <AlertTriangle size={24} strokeWidth={1.5} aria-hidden style={{ opacity: 0.5 }} />
        {tab.errorMessage}
      </div>
    )
  }

  const suspenseFallback = (
    <div style={{ padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
      {t('quickOpen.loading')}
    </div>
  )

  return (
    <div style={{ height: '100%', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      <Suspense fallback={suspenseFallback}>
        {tab.kind === 'browser' && (
          <LazyBrowserViewerTab tabId={tab.id} />
        )}
        {tab.kind === 'terminal' && (
          <LazyTerminalViewerTab tabId={tab.id} />
        )}
        {tab.kind === 'file' && tab.contentClass === 'text' && (
          isMarkdownPath(tab.absolutePath)
            ? <LazyMarkdownViewer absolutePath={tab.absolutePath} />
            : <LazyTextViewer absolutePath={tab.absolutePath} />
        )}
        {tab.kind === 'file' && tab.contentClass === 'image' && (
          <LazyImageViewer absolutePath={tab.absolutePath} sizeBytes={tab.sizeBytes} />
        )}
        {tab.kind === 'file' && tab.contentClass === 'pdf' && (
          <LazyPdfViewer absolutePath={tab.absolutePath} />
        )}
        {tab.kind === 'file' && tab.contentClass === 'unsupported' && (
          <LazyUnsupportedViewer filePath={tab.absolutePath} />
        )}
      </Suspense>
    </div>
  )
}
