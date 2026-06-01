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
import { useEffect, useRef, useState } from 'react'
import MonacoEditor, { loader, type OnMount } from '@monaco-editor/react'
import * as monaco from 'monaco-editor'
import { useT } from '../../../contexts/LocaleContext'
import type { FileNavigationHint } from '../../../../shared/viewer/types'
import { detectLanguage } from './languageDetect'
import { getMonacoTheme, useDocumentThemeMode } from './viewerTheme'

const MAX_READ_BYTES = 5 * 1024 * 1024 // 5 MB

loader.config({ monaco })

interface TextViewerProps {
  absolutePath: string
  /** Word-wrap preference; undefined is treated as enabled (historical default). */
  wordWrap?: boolean
  navigationHint?: FileNavigationHint
}

interface TextState {
  status: 'idle' | 'loading' | 'ok' | 'error'
  text: string
  truncated: boolean
  absolutePath?: string
  error?: string
}

interface MonacoPosition {
  lineNumber: number
  column: number
}

function normalizeNavigationPosition(
  model: monaco.editor.ITextModel,
  hint?: FileNavigationHint
): MonacoPosition | null {
  const rawLine = hint?.line
  if (rawLine === undefined || !Number.isFinite(rawLine) || rawLine < 1) {
    return null
  }

  const lineCount = Math.max(1, model.getLineCount())
  const lineNumber = Math.min(Math.max(1, Math.floor(rawLine)), lineCount)
  const hintedColumn = hint?.column
  const rawColumn = hintedColumn !== undefined && Number.isFinite(hintedColumn)
    ? Math.floor(hintedColumn)
    : 1
  const maxColumn = Math.max(1, model.getLineMaxColumn(lineNumber))
  const column = Math.min(Math.max(1, rawColumn), maxColumn)

  return { lineNumber, column }
}

export function TextViewer({
  absolutePath,
  wordWrap = true,
  navigationHint
}: TextViewerProps): JSX.Element {
  const t = useT()
  const themeMode = useDocumentThemeMode()
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null)
  const [editorReady, setEditorReady] = useState(false)
  const [state, setState] = useState<TextState>({ status: 'idle', text: '', truncated: false })

  useEffect(() => {
    let cancelled = false
    editorRef.current = null
    setEditorReady(false)
    setState({ status: 'loading', text: '', truncated: false, absolutePath })

    window.api.workspace.viewer.readText({ absolutePath, limitBytes: MAX_READ_BYTES })
      .then((result) => {
        if (cancelled) return
        setState({ status: 'ok', text: result.text, truncated: result.truncated, absolutePath })
      })
      .catch((err: unknown) => {
        if (cancelled) return
        const msg = err instanceof Error ? err.message : String(err)
        setState({ status: 'error', text: '', truncated: false, absolutePath, error: msg })
      })

    return () => {
      cancelled = true
    }
  }, [absolutePath])

  useEffect(() => {
    if (state.status !== 'ok' || state.absolutePath !== absolutePath || !editorReady) return
    const editor = editorRef.current
    const model = editor?.getModel()
    if (!editor || !model) return
    const position = normalizeNavigationPosition(model, navigationHint)
    if (!position) return
    editor.setPosition(position)
    editor.revealPositionInCenter(position)
  }, [absolutePath, editorReady, navigationHint, state.absolutePath, state.status, state.text])

  const handleEditorMount: OnMount = (editor) => {
    editorRef.current = editor
    setEditorReady(true)
  }

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
          onMount={handleEditorMount}
          options={{
            readOnly: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            wordWrap: wordWrap ? 'on' : 'off',
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
