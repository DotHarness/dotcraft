/**
 * Read-only text viewer using Monaco Editor.
 *
 * Features:
 *  - Read-only mode, no editing possible.
 *  - Syntax highlighting via language detection.
 *  - Shows a "truncated" notice banner when the file was too large.
 *  - Loading and error states.
 *
 * References: orca/src/renderer/src/components/editor/MonacoEditor.tsx
 */
import { useEffect, useState } from 'react'
import MonacoEditor, { loader } from '@monaco-editor/react'
import * as monaco from 'monaco-editor'
import { useT } from '../../../contexts/LocaleContext'
import { detectLanguage } from './languageDetect'
import { getMonacoTheme, useDocumentThemeMode } from './viewerTheme'

const MAX_READ_BYTES = 5 * 1024 * 1024 // 5 MB

loader.config({ monaco })

interface TextViewerProps {
  absolutePath: string
}

interface TextState {
  status: 'idle' | 'loading' | 'ok' | 'error'
  text: string
  truncated: boolean
  error?: string
}

export function TextViewer({ absolutePath }: TextViewerProps): JSX.Element {
  const t = useT()
  const themeMode = useDocumentThemeMode()
  const [state, setState] = useState<TextState>({ status: 'idle', text: '', truncated: false })

  useEffect(() => {
    let cancelled = false
    setState({ status: 'loading', text: '', truncated: false })

    window.api.workspace.viewer.readText({ absolutePath, limitBytes: MAX_READ_BYTES })
      .then((result) => {
        if (cancelled) return
        setState({ status: 'ok', text: result.text, truncated: result.truncated })
      })
      .catch((err: unknown) => {
        if (cancelled) return
        const msg = err instanceof Error ? err.message : String(err)
        setState({ status: 'error', text: '', truncated: false, error: msg })
      })

    return () => {
      cancelled = true
    }
  }, [absolutePath])

  const language = detectLanguage(absolutePath)

  if (state.status === 'loading') {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: '13px'
      }}>
        {t('quickOpen.loading')}
      </div>
    )
  }

  if (state.status === 'error') {
    return (
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        color: 'var(--text-secondary)',
        fontSize: '13px',
        padding: '24px',
        textAlign: 'center'
      }}>
        {t('viewer.readFailed')} — {state.error}
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      {state.truncated && (
        <div
          role="status"
          style={{
            padding: '4px 12px',
            backgroundColor: 'var(--bg-warning, rgba(255,200,0,0.12))',
            color: 'var(--text-warning, #e8c000)',
            fontSize: '12px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          {t('viewer.truncatedNotice')}
        </div>
      )}
      <div style={{ flex: 1, overflow: 'hidden' }}>
        <MonacoEditor
          language={language}
          value={state.text}
          options={{
            readOnly: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            wordWrap: 'on',
            fontSize: 13,
            lineNumbers: 'on',
            renderWhitespace: 'none',
            contextmenu: false,
            overviewRulerLanes: 0,
            hideCursorInOverviewRuler: true,
            overviewRulerBorder: false,
            scrollbar: {
              verticalScrollbarSize: 8,
              horizontalScrollbarSize: 8
            }
          }}
          theme={getMonacoTheme(themeMode)}
          height="100%"
          loading={
            <div style={{ padding: '24px', color: 'var(--text-secondary)', fontSize: '13px' }}>
              {t('quickOpen.loading')}
            </div>
          }
        />
      </div>
    </div>
  )
}
