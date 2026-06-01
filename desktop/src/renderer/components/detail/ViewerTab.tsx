/**
 * ViewerTab — the container component rendered in the detail panel body when a
 * viewer tab is active.
 *
 * Looks up the tab descriptor from `viewerTabStore` and routes to the
 * appropriate sub-viewer (text, image, pdf, unsupported).  All sub-viewers
 * are `React.lazy`-loaded to keep Monaco out of the initial bundle.
 *
 * File tabs are framed by a `ViewerHeader` (breadcrumb + actions + Open) and an
 * optional docked `WorkspaceExplorer`. Browser and terminal tabs render
 * full-bleed with their own chrome.
 */
import { lazy, Suspense, useCallback } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useUIStore } from '../../stores/uiStore'
import { AlertTriangle } from 'lucide-react'
import { ViewerHeader } from './ViewerHeader'
import { WorkspaceExplorer } from './WorkspaceExplorer'
import { DragHandle } from '../layout/DragHandle'

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
  const setWordWrap = useViewerTabStore((s) => s.setWordWrap)
  const explorerVisible = useUIStore((s) => s.explorerVisible)
  const explorerWidth = useUIStore((s) => s.explorerWidth)

  const handleExplorerDrag = useCallback((delta: number) => {
    const state = useUIStore.getState()
    state.setExplorerWidth(state.explorerWidth - delta)
  }, [])

  if (!tab) {
    return <CenteredNotice message={t('viewer.missingFile')} />
  }

  if (tab.errorMessage) {
    return <CenteredNotice message={tab.errorMessage} />
  }

  const suspenseFallback = (
    <div style={{ padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
      {t('quickOpen.loading')}
    </div>
  )

  // Browser / terminal tabs keep their own chrome and fill the panel.
  if (tab.kind === 'browser' || tab.kind === 'terminal') {
    return (
      <div style={{ height: '100%', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        <Suspense fallback={suspenseFallback}>
          {tab.kind === 'browser'
            ? <LazyBrowserViewerTab tabId={tab.id} />
            : <LazyTerminalViewerTab tabId={tab.id} />}
        </Suspense>
      </div>
    )
  }

  const markdown = isMarkdownPath(tab.absolutePath)
  const isPlainText = tab.contentClass === 'text' && !markdown
  const wordWrap = tab.wordWrap !== false

  return (
    <div style={{ height: '100%', overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
      <ViewerHeader
        absolutePath={tab.absolutePath}
        relativePath={tab.relativePath}
        isText={isPlainText}
        wordWrap={wordWrap}
        onToggleWordWrap={() => {
          if (currentThreadId) setWordWrap(currentThreadId, tab.id, !wordWrap)
        }}
      />
      <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'row' }}>
        <div style={{ flex: '1 1 0', minWidth: 160, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
          <Suspense fallback={suspenseFallback}>
            {tab.contentClass === 'text' && (
              markdown
                ? <LazyMarkdownViewer absolutePath={tab.absolutePath} />
                : <LazyTextViewer
                    absolutePath={tab.absolutePath}
                    wordWrap={wordWrap}
                    navigationHint={tab.navigationHint}
                  />
            )}
            {tab.contentClass === 'image' && (
              <LazyImageViewer absolutePath={tab.absolutePath} sizeBytes={tab.sizeBytes} />
            )}
            {tab.contentClass === 'pdf' && (
              <LazyPdfViewer absolutePath={tab.absolutePath} />
            )}
            {tab.contentClass === 'unsupported' && (
              <LazyUnsupportedViewer filePath={tab.absolutePath} />
            )}
          </Suspense>
        </div>

        {explorerVisible && (
          <>
            <DragHandle onDrag={handleExplorerDrag} />
            <div
              style={{
                flex: `0 1 ${explorerWidth}px`,
                minWidth: 140,
                overflow: 'hidden',
                display: 'flex',
                flexDirection: 'column',
                borderLeft: '1px solid var(--glass-border)'
              }}
            >
              <WorkspaceExplorer />
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function CenteredNotice({ message }: { message: string }): JSX.Element {
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
      {message}
    </div>
  )
}
