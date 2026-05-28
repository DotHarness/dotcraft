import { memo, type CSSProperties } from 'react'
import { ExternalLink, FileText, Globe2 } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import type { FileDiff } from '../../types/toolCall'
import { toAbsPath } from '../../hooks/useFileChangeActions'
import { OpenTargetButton } from './OpenTargetButton'

interface TurnArtifactsProps {
  turnId: string
}

type ArtifactKind = 'markdown' | 'html'

interface Artifact {
  kind: ArtifactKind
  diff: FileDiff
}

export const TurnArtifacts = memo(function TurnArtifacts({ turnId }: TurnArtifactsProps): JSX.Element | null {
  const t = useT()
  const changedFiles = useConversationStore((s) => s.changedFiles)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const currentThreadId = useViewerTabStore((s) => s.currentThreadId)
  const openFile = useViewerTabStore((s) => s.openFile)
  const openBrowser = useViewerTabStore((s) => s.openBrowser)
  const focusBrowserTabByUrl = useViewerTabStore((s) => s.focusBrowserTabByUrl)
  const setActiveViewerTab = useUIStore((s) => s.setActiveViewerTab)
  const setDetailPanelVisible = useUIStore((s) => s.setDetailPanelVisible)

  const artifacts = Array.from(changedFiles.values())
    .filter((file) => file.status === 'written' && turnIncludesFile(file, turnId))
    .map(toArtifact)
    .filter((item): item is Artifact => item !== null)

  if (artifacts.length === 0) return null

  async function openLocalHtml(diff: FileDiff): Promise<void> {
    if (!currentThreadId || !workspacePath) return
    const absPath = toAbsPath(diff.filePath, workspacePath)
    try {
      const { url } = await window.api.workspace.viewer.toViewerUrl({ absolutePath: absPath })
      const existing = focusBrowserTabByUrl({ threadId: currentThreadId, url })
      if (existing) {
        setActiveViewerTab(existing)
        return
      }
      const tabId = openBrowser({
        threadId: currentThreadId,
        target: `local-html:${absPath}`,
        initialUrl: url,
        initialLabel: basename(diff.filePath)
      })
      setActiveViewerTab(tabId)
      await window.api.workspace.viewer.browser.create({
        tabId,
        threadId: currentThreadId,
        workspacePath,
        initialUrl: url
      })
    } catch (err) {
      console.error('Failed to open HTML preview:', err)
    }
  }

  async function openLocalFile(diff: FileDiff): Promise<void> {
    if (!currentThreadId || !workspacePath) return
    const absPath = toAbsPath(diff.filePath, workspacePath)
    try {
      const classified = await window.api.workspace.viewer.classify({ absolutePath: absPath })
      const tabId = openFile({
        threadId: currentThreadId,
        absolutePath: absPath,
        relativePath: diff.filePath,
        contentClass: classified.contentClass,
        sizeBytes: classified.sizeBytes
      })
      setActiveViewerTab(tabId)
      setDetailPanelVisible(true)
    } catch (err) {
      console.error('Failed to open file artifact:', err)
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' }}>
      {artifacts.map(({ kind, diff }) => {
        const name = basename(diff.filePath)
        const absPath = workspacePath ? toAbsPath(diff.filePath, workspacePath) : diff.filePath
        const isHtml = kind === 'html'
        const handleCardOpen = (): void => {
          void (isHtml ? openLocalHtml(diff) : openLocalFile(diff))
        }
        return (
          <div
            key={`${kind}:${diff.filePath}`}
            style={artifactCardStyle}
          >
            <button
              type="button"
              aria-label={isHtml
                ? t('turnArtifacts.openHtmlInBrowserAria', { file: name })
                : t('turnArtifacts.openInViewerAria', { file: name })}
              onClick={handleCardOpen}
              style={artifactBodyButtonStyle}
            >
              <span style={artifactIconStyle}>
                {isHtml ? <Globe2 size={22} strokeWidth={1.8} /> : <FileText size={22} strokeWidth={1.8} />}
              </span>
              <span style={artifactTextWrapStyle}>
                <span style={artifactTitleStyle}>
                  {name}
                </span>
                <span style={artifactSubtitleStyle}>
                  {isHtml ? t('turnArtifacts.htmlSubtitle') : t('turnArtifacts.markdownSubtitle')}
                </span>
              </span>
            </button>
            {isHtml ? (
              <button
                type="button"
                aria-label={t('turnArtifacts.previewAria', { file: name })}
                onClick={() => { void openLocalHtml(diff) }}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '6px',
                  minHeight: '32px',
                  padding: '5px 12px',
                  borderRadius: '8px',
                  border: '1px solid var(--border-default)',
                  background: 'transparent',
                  color: 'var(--text-primary)',
                  cursor: 'pointer',
                  fontSize: '13px',
                  fontWeight: 500,
                  flexShrink: 0
                }}
              >
                <ExternalLink size={15} strokeWidth={1.8} aria-hidden />
                {t('turnArtifacts.preview')}
              </button>
            ) : (
              <OpenTargetButton
                targetPath={absPath}
                tooltipLabel={t('turnArtifacts.openTitle', { path: diff.filePath })}
                menuAriaLabel={t('turnArtifacts.openMenuAria')}
                showPrimaryLabel
                primaryButtonLabel={t('threadHeader.open')}
              />
            )}
          </div>
        )
      })}
    </div>
  )
})

function turnIncludesFile(file: FileDiff, turnId: string): boolean {
  const ids = file.turnIds?.length ? file.turnIds : [file.turnId]
  return ids.includes(turnId)
}

function toArtifact(diff: FileDiff): Artifact | null {
  const ext = extensionOf(diff.filePath)
  if (ext === '.md' || ext === '.markdown') return { kind: 'markdown', diff }
  if (ext === '.html' || ext === '.htm') return { kind: 'html', diff }
  return null
}

function basename(filePath: string): string {
  return filePath.split(/[\\/]/).pop() ?? filePath
}

function extensionOf(filePath: string): string {
  const name = basename(filePath).toLowerCase()
  const dot = name.lastIndexOf('.')
  return dot >= 0 ? name.slice(dot) : ''
}

const artifactCardStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '0 14px 0 0',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-primary)',
  boxShadow: '0 1px 2px rgba(0, 0, 0, 0.04)',
  overflow: 'hidden'
}

const artifactBodyButtonStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  minHeight: '68px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '12px 0 12px 14px',
  border: 'none',
  background: 'transparent',
  color: 'inherit',
  cursor: 'pointer',
  textAlign: 'left',
  font: 'inherit'
}

const artifactIconStyle: CSSProperties = {
  width: '44px',
  height: '44px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

const artifactTextWrapStyle: CSSProperties = {
  minWidth: 0,
  flex: 1,
  display: 'block'
}

const artifactTitleStyle: CSSProperties = {
  display: 'block',
  fontSize: '14px',
  fontWeight: 600,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const artifactSubtitleStyle: CSSProperties = {
  display: 'block',
  marginTop: '2px',
  fontSize: '12px',
  color: 'var(--text-secondary)'
}
