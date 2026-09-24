import { useEffect, useRef, useState } from 'react'
import {
  EditorState,
  StateEffect,
  Transaction,
  Annotation,
  type Extension
} from '@codemirror/state'
import {
  EditorView,
  drawSelection,
  dropCursor,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
  rectangularSelection
} from '@codemirror/view'
import {
  defaultKeymap,
  history,
  historyKeymap,
  indentWithTab,
  redo,
  redoDepth,
  undo,
  undoDepth
} from '@codemirror/commands'
import { search, gotoLine } from '@codemirror/search'
import { markdown } from '@codemirror/lang-markdown'
import { GFM } from '@lezer/markdown'
import { AlertCircle, LoaderCircle, Redo2, Undo2 } from 'lucide-react'
import type { FileNavigationHint } from '../../../../shared/viewer/types'
import { useT } from '../../../contexts/LocaleContext'
import { useDocumentThemeMode } from '../../../utils/theme'
import { useHighlighterPool } from '../../../highlight'
import { useFindStore } from '../../../stores/findStore'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { useFileEditorStore } from '../../../stores/fileEditorStore'
import { openConversationLink } from '../../../utils/conversationDeepLink'
import { IconButton } from '../../ui/IconButton'
import { editorHighlight } from './editorHighlight'
import { markdownEditing } from './markdownEditing'
import { EditorFind } from './EditorFind'
import { FileReview } from './FileReview'
import { FileEditorComments, lineCommentField } from './FileEditorComments'
import './file-editor.css'

interface FileEditorProps {
  tabId: string
  absolutePath: string
  markdown: boolean
  wordWrap: boolean
  navigationHint?: FileNavigationHint
}
const externalUpdate = Annotation.define<boolean>()

export function FileEditor({
  tabId,
  absolutePath,
  markdown: isMarkdown,
  wordWrap,
  navigationHint
}: FileEditorProps): JSX.Element {
  const t = useT()
  const theme = useDocumentThemeMode()
  const pool = useHighlighterPool()
  const host = useRef<HTMLDivElement>(null)
  const viewRef = useRef<EditorView | null>(null)
  const [revision, setRevision] = useState(0)
  const [finding, setFinding] = useState(false)
  const session = useFileEditorStore((state) => state.sessions.get(tabId))
  const mode = session?.mode ?? (isMarkdown ? 'preview' : 'source')
  const preview = isMarkdown && mode === 'preview'
  const readOnly = Boolean(session?.readOnlyReason || session?.review)

  useEffect(() => {
    void useFileEditorStore.getState().load(tabId, absolutePath, isMarkdown)
  }, [tabId, absolutePath, isMarkdown])

  const extensions = (): Extension[] => [
    ...(preview
      ? [
          markdown({ extensions: GFM }),
          markdownEditing(theme, (href) => {
            const viewer = useViewerTabStore.getState()
            if (!viewer.currentThreadId || !viewer.currentWorkspacePath) return
            void openConversationLink({
              target: href,
              workspacePath: viewer.currentWorkspacePath,
              threadId: viewer.currentThreadId,
              t
            })
          })
        ]
      : [lineNumbers(), highlightActiveLineGutter()]),
    history(),
    drawSelection(),
    dropCursor(),
    rectangularSelection(),
    highlightActiveLine(),
    EditorState.tabSize.of(4),
    EditorState.readOnly.of(readOnly),
    EditorView.editable.of(!readOnly),
    editorHighlight(pool, absolutePath, preview),
    lineCommentField,
    search(),
    ...(wordWrap || preview ? [EditorView.lineWrapping] : []),
    EditorView.contentAttributes.of({
      'aria-label': absolutePath,
      tabindex: '0',
      spellcheck: preview ? 'true' : 'false'
    }),
    EditorView.updateListener.of((update) => {
      if (update.docChanged && !update.transactions.some((tr) => tr.annotation(externalUpdate))) {
        useFileEditorStore.getState().updateText(tabId, update.state.doc.toString())
      }
      if (update.docChanged || update.selectionSet || update.focusChanged)
        setRevision((value) => value + 1)
    }),
    EditorView.domEventHandlers({
      keydown(event) {
        if (
          (event.ctrlKey || event.metaKey) &&
          event.key.toLowerCase() === 'f' &&
          !event.isComposing
        ) {
          event.preventDefault()
          event.stopPropagation()
          useFindStore.getState().closeFind()
          setFinding(true)
          return true
        }
        return false
      }
    }),
    keymap.of([
      {
        key: 'Mod-s',
        preventDefault: true,
        run: () => {
          void useFileEditorStore.getState().save(tabId)
          return true
        }
      },
      { key: 'Alt-g', run: gotoLine },
      indentWithTab,
      ...defaultKeymap,
      ...historyKeymap
    ]),
    EditorView.theme({}, { dark: theme === 'dark' })
  ]
  const extensionsRef = useRef(extensions)
  extensionsRef.current = extensions

  useEffect(() => {
    const current = useFileEditorStore.getState().sessions.get(tabId)
    if (!host.current || current?.status !== 'ready') return
    const snapshot = current.snapshots[mode]
    const config = extensionsRef.current()
    const validSnapshot =
      snapshot?.version === current.version && snapshot.state.doc.toString() === current.text
        ? snapshot
        : undefined
    const state = validSnapshot
      ? validSnapshot.state.update({ effects: StateEffect.reconfigure.of(config) }).state
      : EditorState.create({ doc: current.text, extensions: config })
    const view = new EditorView({ state, parent: host.current })
    viewRef.current = view
    setRevision((value) => value + 1)
    const frame = requestAnimationFrame(() => {
      view.scrollDOM.scrollTop = validSnapshot?.scrollTop ?? 0
      view.scrollDOM.scrollLeft = validSnapshot?.scrollLeft ?? 0
    })
    return () => {
      cancelAnimationFrame(frame)
      const latest = useFileEditorStore.getState().sessions.get(tabId)
      if (latest?.text === view.state.doc.toString()) {
        useFileEditorStore.getState().setSnapshot(tabId, mode, {
          version: latest.version,
          state: view.state,
          scrollTop: view.scrollDOM.scrollTop,
          scrollLeft: view.scrollDOM.scrollLeft
        })
      }
      view.destroy()
      viewRef.current = null
    }
  }, [absolutePath, tabId, session?.status, mode])

  useEffect(() => {
    viewRef.current?.dispatch({ effects: StateEffect.reconfigure.of(extensionsRef.current()) })
  }, [theme, pool, readOnly, wordWrap, mode])

  useEffect(() => {
    const view = viewRef.current
    if (
      !view ||
      !session ||
      session.status !== 'ready' ||
      view.state.doc.toString() === session.text
    )
      return
    const old = view.state.doc.toString()
    const next = session.text
    let from = 0
    while (from < old.length && from < next.length && old[from] === next[from]) from++
    let end = 0
    while (
      end < old.length - from &&
      end < next.length - from &&
      old[old.length - end - 1] === next[next.length - end - 1]
    )
      end++
    view.dispatch({
      changes: { from, to: old.length - end, insert: next.slice(from, next.length - end) },
      annotations: [externalUpdate.of(true), Transaction.addToHistory.of(false)]
    })
  }, [session?.text, session?.status, mode])

  useEffect(() => {
    if (session?.focusRevision) viewRef.current?.focus()
  }, [session?.focusRevision])

  useEffect(() => {
    const view = viewRef.current
    if (!view || !navigationHint?.line || !Number.isFinite(navigationHint.line)) return
    const line = view.state.doc.line(
      Math.min(view.state.doc.lines, Math.max(1, Math.floor(navigationHint.line)))
    )
    const column = Number.isFinite(navigationHint.column) ? navigationHint.column! : 1
    const anchor = Math.min(line.to, line.from + Math.max(0, Math.floor(column) - 1))
    view.dispatch({
      selection: { anchor },
      effects: EditorView.scrollIntoView(anchor, { y: 'center' })
    })
  }, [session?.status, navigationHint?.line, navigationHint?.column])

  const view = viewRef.current
  const undoCount = view ? undoDepth(view.state) : 0
  const redoCount = view ? redoDepth(view.state) : 0
  const status = preview && (session?.saveState === 'saving' || session?.saveState === 'failed')
  const showToolbar = !readOnly && (undoCount > 0 || redoCount > 0 || status)
  if (!session || session.status === 'loading')
    return <div className="dc-file-editor__centered">{t('quickOpen.loading')}</div>
  if (session.status === 'error')
    return (
      <div className="dc-file-editor__centered">
        {t('viewer.readFailed')} — {session.error}
      </div>
    )

  return (
    <div
      className={`dc-file-editor dc-code${preview ? ' dc-file-editor--markdown' : ''}`}
      data-file-editor
    >
      {session.readOnlyReason && (
        <div className="dc-file-editor__notice" role="status">
          {t('viewer.largeFileReadOnly')}
        </div>
      )}
      <div
        ref={host}
        className="dc-file-editor__host"
        inert={Boolean(session.review)}
        aria-hidden={Boolean(session.review)}
      />
      {!session.review && (
        <FileEditorComments view={view} tabId={tabId} absolutePath={absolutePath} preview={preview} />
      )}
      {finding && view && !session.review && (
        <EditorFind view={view} revision={revision} onClose={() => setFinding(false)} />
      )}
      {showToolbar && (
        <div className="dc-file-editor__toolbar" role="status">
          <IconButton
            size={24}
            radius={6}
            label={t('viewer.undo')}
            disabled={!undoCount}
            onClick={() => {
              if (view) undo(view)
            }}
            icon={<Undo2 size={14} aria-hidden />}
          />
          <IconButton
            size={24}
            radius={6}
            label={t('viewer.redo')}
            disabled={!redoCount}
            onClick={() => {
              if (view) redo(view)
            }}
            icon={<Redo2 size={14} aria-hidden />}
          />
          {preview && session.saveState === 'saving' && (
            <span>
              <LoaderCircle size={14} aria-hidden />
              {t('viewer.saving')}
            </span>
          )}
          {preview && session.saveState === 'failed' && (
            <span className="dc-file-editor__save-error">
              <AlertCircle size={14} aria-hidden />
              {t('viewer.saveFailed')}
            </span>
          )}
        </div>
      )}
      {session.review && (
        <FileReview tabId={tabId} path={absolutePath} review={session.review} wordWrap={wordWrap} />
      )}
    </div>
  )
}
